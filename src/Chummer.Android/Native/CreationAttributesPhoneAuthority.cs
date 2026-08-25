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
        return MatchesOverview(state, overview) && StateShapeIsReady(state);
    }

    private static bool StateShapeIsReady(CharacterCreationAttributesState state)
    {
        if (!string.Equals(
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
        if (!string.Equals(
                result.Outcome,
                CharacterCreationFoundationOutcomes.Success,
                StringComparison.Ordinal)
            || !IsReady(state, overview)
            || result.Value is not { } preview
            || !BindingEquals(state.Binding, preview.Binding)
            || !AllocationIdentitiesMatch(state, allocations)
            || !ProjectionIdentitiesMatch(state, preview.Attributes)
            || !PreviewMatchesAllocations(preview, allocations)
            || !preview.RequiresExplicitConfirmation
            || !preview.CanConfirm
            || result.Blockers.Count != 0
            || preview.Blockers.Count != 0
            || !BudgetIsExact(preview.NormalPointBudget)
            || !BudgetIsExact(preview.SpecialPointBudget)
            || !BudgetIsExact(preview.CreationKarmaBudget)
            || !IsCanonicalDigest(preview.PreviewDigest))
        {
            return false;
        }

        return true;
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

    /// <summary>
    /// Compares the complete immutable projection returned by Core. A canonical digest is
    /// necessary but not sufficient at the Android trust boundary: the allocation projection,
    /// budgets, blockers, confirmation flags, binding, and digest must all be the exact values
    /// Core just re-projected for this confirmation attempt.
    /// </summary>
    public static bool CanonicallyEquals(
        CharacterCreationAttributesPreview left,
        CharacterCreationAttributesPreview right)
        => string.Equals(left.Schema, right.Schema, StringComparison.Ordinal)
           && BindingEquals(left.Binding, right.Binding)
           && ProjectionsEqual(left.Attributes, right.Attributes)
           && BudgetsEqual(left.NormalPointBudget, right.NormalPointBudget)
           && BudgetsEqual(left.SpecialPointBudget, right.SpecialPointBudget)
           && BudgetsEqual(left.CreationKarmaBudget, right.CreationKarmaBudget)
           && left.Blockers.SequenceEqual(right.Blockers, StringComparer.Ordinal)
           && left.RequiresExplicitConfirmation == right.RequiresExplicitConfirmation
           && left.CanConfirm == right.CanConfirm
           && DigestEquals(left.PreviewDigest, right.PreviewDigest);

    public static bool ReceiptMatchesBeforeActivation(
        CharacterCreationAttributesReceipt receipt,
        CharacterCreationAttributesPreview preview,
        IReadOnlyList<CharacterCreationAttributeAllocation> allocations,
        CharacterCreationAttributesState refreshed,
        CharacterOverviewState beforeActivation)
        => BindingMatchesOverview(preview.Binding, beforeActivation)
           && ReceiptMatchesAuthoritativeState(
               receipt,
               preview,
               allocations,
               refreshed);

    public static bool ReceiptMatches(
        CharacterCreationAttributesReceipt receipt,
        CharacterCreationAttributesPreview preview,
        IReadOnlyList<CharacterCreationAttributeAllocation> allocations,
        CharacterCreationAttributesState refreshed,
        CharacterOverviewState overview)
        => IsReady(refreshed, overview)
           && ReceiptMatchesAuthoritativeState(
               receipt,
               preview,
               allocations,
               refreshed);

    private static bool ReceiptMatchesAuthoritativeState(
        CharacterCreationAttributesReceipt receipt,
        CharacterCreationAttributesPreview preview,
        IReadOnlyList<CharacterCreationAttributeAllocation> allocations,
        CharacterCreationAttributesState refreshed)
        => StateShapeIsReady(refreshed)
           && refreshed.PendingDraft is { } pending
           && PreviewMatchesAllocations(preview, allocations)
           && preview.RequiresExplicitConfirmation
           && preview.CanConfirm
           && preview.Blockers.Count == 0
           && IsCanonicalDigest(preview.PreviewDigest)
           && receipt.WorkspaceId == preview.Binding.WorkspaceId
           && receipt.WorkspaceId == refreshed.Binding.WorkspaceId
           && receipt.PreviousContentRevision == preview.Binding.ContentRevision
           && receipt.PreviousContentRevision >= 0
           && receipt.PreviousContentRevision < long.MaxValue
           && receipt.ContentRevision == receipt.PreviousContentRevision + 1
           && receipt.ContentRevision == refreshed.Binding.ContentRevision
           && receipt.SavedRevision == refreshed.Binding.SavedRevision
           && refreshed.Binding.PrerequisiteDraftRevision
               == preview.Binding.PrerequisiteDraftRevision
           && DigestEquals(
               refreshed.Binding.PrerequisiteDraftDigest,
               preview.Binding.PrerequisiteDraftDigest)
           && DigestEquals(
               refreshed.Binding.PrerequisiteAuthorityDigest,
               preview.Binding.PrerequisiteAuthorityDigest)
           && !DigestEquals(
               refreshed.Binding.AuxiliaryStateDigest,
               preview.Binding.AuxiliaryStateDigest)
           && receipt.DraftRevision == pending.DraftRevision
           && DigestEquals(receipt.DraftDigest, pending.DraftDigest)
           && IsCanonicalDigest(receipt.DraftDigest)
           && pending.BaseContentRevision == receipt.PreviousContentRevision
           && AllocationsEqual(pending.Allocations, allocations)
           && ProjectionsEqual(pending.Attributes, preview.Attributes)
           && ProjectionsEqual(refreshed.Attributes, preview.Attributes)
           && BudgetsEqual(refreshed.NormalPointBudget, preview.NormalPointBudget)
           && BudgetsEqual(refreshed.SpecialPointBudget, preview.SpecialPointBudget)
           && BudgetsEqual(refreshed.CreationKarmaBudget, preview.CreationKarmaBudget)
           && pending.NormalPointTotal == preview.NormalPointBudget.Total
           && pending.NormalPointUsed == preview.NormalPointBudget.Used
           && pending.SpecialPointTotal == preview.SpecialPointBudget.Total
           && pending.SpecialPointUsed == preview.SpecialPointBudget.Used
           && pending.CreationKarmaTotal == preview.CreationKarmaBudget.Total
           && pending.CreationKarmaUsed == preview.CreationKarmaBudget.Used
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
           && !string.IsNullOrWhiteSpace(pending.MetatypeSourceId)
           && IsCanonicalDigest(pending.MetatypeSourceNodeDigest)
           && pending.SourceAnchorIds.Count > 0
           && pending.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor))
           && !pending.CharacterEffectsApplied
           && pending.Attributes.Count == state.Attributes.Count
           && pending.Allocations.Count == state.Attributes.Count
           && ProjectionIdentitiesMatch(state, pending.Attributes)
           && AllocationIdentitiesMatch(state, pending.Allocations)
           && ProjectionsEqual(pending.Attributes, state.Attributes)
           && PreviewProjectionsMatchAllocations(pending.Attributes, pending.Allocations)
           && pending.NormalPointTotal == state.NormalPointBudget.Total
           && pending.NormalPointUsed == state.NormalPointBudget.Used
           && pending.SpecialPointTotal == state.SpecialPointBudget.Total
           && pending.SpecialPointUsed == state.SpecialPointBudget.Used
           && pending.CreationKarmaTotal == state.CreationKarmaBudget.Total
           && pending.CreationKarmaUsed == state.CreationKarmaBudget.Used
           && pending.Allocations.Select(allocation => allocation.AttributeId)
               .Distinct(StringComparer.Ordinal).Count() == pending.Allocations.Count;

    private static bool BindingMatchesOverview(
        CharacterCreationAttributesBinding binding,
        CharacterOverviewState overview)
        => overview.Profile?.Created == false
           && overview.WorkspaceId == binding.WorkspaceId
           && overview.ContentRevision == binding.ContentRevision
           && overview.SavedRevision == binding.SavedRevision
           && IsCanonicalDigest(binding.RawCharacterXmlDigest)
           && IsCanonicalAuxiliaryDigest(binding.AuxiliaryStateDigest)
           && IsCanonicalDigest(binding.PrerequisiteDraftDigest)
           && IsCanonicalDigest(binding.PrerequisiteAuthorityDigest);

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

    private static bool BudgetsEqual(
        CharacterCreationBudgetState left,
        CharacterCreationBudgetState right)
        => string.Equals(left.BudgetId, right.BudgetId, StringComparison.Ordinal)
           && string.Equals(left.Label, right.Label, StringComparison.Ordinal)
           && left.Total == right.Total
           && left.Used == right.Used
           && left.Remaining == right.Remaining
           && left.IsExact == right.IsExact
           && left.Blockers.SequenceEqual(right.Blockers, StringComparer.Ordinal)
           && string.Equals(left.Unit, right.Unit, StringComparison.Ordinal);

    private static bool AllocationsEqual(
        IReadOnlyList<CharacterCreationAttributeAllocation> left,
        IReadOnlyList<CharacterCreationAttributeAllocation> right)
        => left.Count == right.Count
           && left.Zip(right).All(pair =>
               string.Equals(pair.First.AttributeId, pair.Second.AttributeId, StringComparison.Ordinal)
               && pair.First.PriorityPoints == pair.Second.PriorityPoints
               && pair.First.KarmaLevels == pair.Second.KarmaLevels);

    private static bool ProjectionsEqual(
        IReadOnlyList<CharacterCreationAttributeProjection> left,
        IReadOnlyList<CharacterCreationAttributeProjection> right)
        => left.Count == right.Count
           && left.Zip(right).All(pair => ProjectionEquals(pair.First, pair.Second));

    private static bool ProjectionEquals(
        CharacterCreationAttributeProjection left,
        CharacterCreationAttributeProjection right)
        => string.Equals(left.AttributeId, right.AttributeId, StringComparison.Ordinal)
           && string.Equals(left.Category, right.Category, StringComparison.Ordinal)
           && left.Minimum == right.Minimum
           && left.Maximum == right.Maximum
           && left.AugmentedMaximum == right.AugmentedMaximum
           && left.Current == right.Current
           && left.PriorityPointsSpent == right.PriorityPointsSpent
           && left.KarmaLevels == right.KarmaLevels
           && left.PriorityPointCost == right.PriorityPointCost
           && left.KarmaCost == right.KarmaCost
           && left.IsEnabled == right.IsEnabled
           && left.DisableReasons.SequenceEqual(right.DisableReasons, StringComparer.Ordinal)
           && left.SourceAnchorIds.SequenceEqual(right.SourceAnchorIds, StringComparer.Ordinal);

    private static bool PreviewProjectionsMatchAllocations(
        IReadOnlyList<CharacterCreationAttributeProjection> projections,
        IReadOnlyList<CharacterCreationAttributeAllocation> allocations)
        => projections.Count == allocations.Count
           && projections.Zip(allocations).All(pair =>
               string.Equals(pair.First.AttributeId, pair.Second.AttributeId, StringComparison.Ordinal)
               && pair.First.PriorityPointsSpent == pair.Second.PriorityPoints
               && pair.First.KarmaLevels == pair.Second.KarmaLevels);

    private static bool DigestEquals(string? left, string? right)
        => CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(left, right);
}
