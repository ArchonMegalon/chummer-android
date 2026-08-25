using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>Validates Core's Skills packets; Android never derives budgets, costs, or legality.</summary>
internal static class CreationSkillsPhoneAuthority
{
    public static bool MatchesOverview(CharacterCreationSkillsState state, CharacterOverviewState overview) =>
        HasValidCoreIntegrity(state)
        && overview.Profile?.Created == false
        && overview.WorkspaceId == state.Binding.WorkspaceId
        && overview.ContentRevision == state.Binding.ContentRevision
        && overview.SavedRevision == state.Binding.SavedRevision
        && Digests(state.Binding);

    public static bool IsReady(CharacterCreationSkillsState state, CharacterOverviewState overview) =>
        MatchesOverview(state, overview)
        && string.Equals(state.Schema, CharacterCreationSkillsSchemas.SnapshotV1, StringComparison.Ordinal)
        && CharacterCreationSkillsDigest.IsCanonical(state.SnapshotDigest)
        && state.CanEdit
        && state.Blockers.Count == 0
        && state.PrerequisiteDraft is { } prerequisite
        && state.AttributesDraft is { } attributes
        && prerequisite.DraftRevision == state.Binding.PrerequisiteDraftRevision
        && attributes.DraftRevision == state.Binding.AttributesDraftRevision
        && Equal(prerequisite.DraftDigest, state.Binding.PrerequisiteDraftDigest)
        && Equal(attributes.DraftDigest, state.Binding.AttributesDraftDigest)
        && Exact(state.ActiveSkillPointBudget)
        && Exact(state.SkillGroupPointBudget)
        && Exact(state.KnowledgeSkillPointBudget)
        && state.Authority.IsAuthoritative
        && state.Authority.Blockers.Count == 0
        && Equal(state.Authority.AuthorityDigest, state.Binding.SkillsAuthorityDigest)
        && Equal(state.Authority.RuntimeDigest, state.Binding.RuntimeDigest)
        && state.Authority.ActiveSkills.Select(item => item.SourceSkillId).Distinct(StringComparer.Ordinal).Count()
           == state.Authority.ActiveSkills.Count
        && state.Authority.KnowledgeSkills.Select(item => item.SourceSkillId).Distinct(StringComparer.Ordinal).Count()
           == state.Authority.KnowledgeSkills.Count;

    private static bool HasValidCoreIntegrity(CharacterCreationSkillsState state) =>
        CharacterCreationSkillsDraftIntegrity.IsValidStateProjection(state)
        && CharacterCreationSkillsDigest.EqualsFixedTime(
            state.Binding.SkillsAuthorityDigest,
            state.Authority.AuthorityDigest)
        && CharacterCreationSkillsDigest.EqualsFixedTime(
            state.Binding.RuntimeDigest,
            state.Authority.RuntimeDigest)
        && CharacterCreationSkillsDigest.EqualsFixedTime(
            state.Binding.ContributionInputsDigest,
            CharacterCreationSkillsDigest.Compute(state.KnowledgePointContributions.ToArray()))
        && CharacterCreationSkillsDigest.EqualsFixedTime(
            state.SnapshotDigest,
            CharacterCreationSkillsDigest.Compute(state with { SnapshotDigest = string.Empty }));

    public static bool BindingEquals(CharacterCreationSkillsBinding left, CharacterCreationSkillsBinding right) =>
        left.WorkspaceId == right.WorkspaceId
        && left.ContentRevision == right.ContentRevision
        && left.SavedRevision == right.SavedRevision
        && left.PrerequisiteDraftRevision == right.PrerequisiteDraftRevision
        && left.AttributesDraftRevision == right.AttributesDraftRevision
        && Equal(left.RawCharacterXmlDigest, right.RawCharacterXmlDigest)
        && Equal(left.AuxiliaryStateDigest, right.AuxiliaryStateDigest)
        && Equal(left.PrerequisiteDraftDigest, right.PrerequisiteDraftDigest)
        && Equal(left.PrerequisiteAuthorityDigest, right.PrerequisiteAuthorityDigest)
        && Equal(left.AttributesDraftDigest, right.AttributesDraftDigest)
        && Equal(left.SkillsAuthorityDigest, right.SkillsAuthorityDigest)
        && Equal(left.RuntimeDigest, right.RuntimeDigest)
        && Equal(left.ContributionInputsDigest, right.ContributionInputsDigest);

    public static bool CanAdoptPreview(
        CharacterCreationSkillsState state,
        CharacterOverviewState overview,
        CharacterCreationFoundationResult<CharacterCreationSkillsPreview> result,
        IReadOnlyList<CharacterCreationSkillAllocation> allocations,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups) =>
        string.Equals(result.Outcome, CharacterCreationFoundationOutcomes.Success, StringComparison.Ordinal)
        && IsReady(state, overview)
        && result.Value is { } preview
        && CanConfirmPreview(state, overview, preview, allocations, groups)
        && result.Blockers.Count == 0;

    public static bool CanConfirmPreview(
        CharacterCreationSkillsState state,
        CharacterOverviewState overview,
        CharacterCreationSkillsPreview preview,
        IReadOnlyList<CharacterCreationSkillAllocation> allocations,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups) =>
        IsReady(state, overview)
        && string.Equals(preview.Schema, CharacterCreationSkillsSchemas.PreviewV1, StringComparison.Ordinal)
        && BindingEquals(state.Binding, preview.Binding)
        && preview.RequiresExplicitConfirmation
        && preview.CanConfirm
        && preview.Blockers.Count == 0
        && Exact(preview.ActiveSkillPointBudget)
        && Exact(preview.SkillGroupPointBudget)
        && Exact(preview.KnowledgeSkillPointBudget)
        && CharacterCreationSkillsDigest.IsCanonical(preview.PreviewDigest)
        && AllocationsEqual(preview.Skills, allocations)
        && GroupsEqual(preview.SkillGroups, groups);

    public static bool CanonicallyEquals(CharacterCreationSkillsPreview left, CharacterCreationSkillsPreview right) =>
        CharacterCreationSkillsDigest.EqualsFixedTime(
            CharacterCreationSkillsDigest.Compute(left),
            CharacterCreationSkillsDigest.Compute(right));

    public static string ComputeIdempotencyKey(
        CharacterCreationSkillsPreview preview,
        IReadOnlyList<CharacterCreationSkillAllocation> allocations,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups) =>
        CharacterCreationSkillsDigest.Compute(new
        {
            Schema = "chummer.android.creation-skills-idempotency.v1",
            preview.Binding.WorkspaceId,
            preview.Binding.ContentRevision,
            Allocations = allocations.OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.SourceSkillId, StringComparer.Ordinal).ToArray(),
            GroupAllocations = groups.OrderBy(item => item.GroupId, StringComparer.Ordinal).ToArray(),
            preview.PreviewDigest
        });

    public static bool ReceiptMatches(
        CharacterCreationSkillsReceipt receipt,
        CharacterCreationSkillsPreview preview,
        CharacterCreationSkillsState refreshed,
        CharacterOverviewState overview,
        string idempotencyKey) =>
        IsReady(refreshed, overview)
        && ReceiptMatchesState(receipt, preview, refreshed, idempotencyKey);

    public static bool ReceiptMatchesBeforeActivation(
        CharacterCreationSkillsReceipt receipt,
        CharacterCreationSkillsPreview preview,
        CharacterCreationSkillsState refreshed,
        CharacterOverviewState beforeActivation,
        string idempotencyKey) =>
        beforeActivation.WorkspaceId == preview.Binding.WorkspaceId
        && beforeActivation.ContentRevision == preview.Binding.ContentRevision
        && beforeActivation.SavedRevision == preview.Binding.SavedRevision
        && string.Equals(refreshed.Schema, CharacterCreationSkillsSchemas.SnapshotV1, StringComparison.Ordinal)
        && refreshed.CanEdit && refreshed.Blockers.Count == 0
        && ReceiptMatchesState(receipt, preview, refreshed, idempotencyKey);

    public static CreationSkillsPhoneConfirmResult CommittedRefreshRequired(
        CharacterCreationSkillsReceipt receipt,
        CharacterCreationSkillsState committedState,
        IEnumerable<string> blockers) => new(
        CharacterCreationFoundationOutcomes.Success,
        receipt,
        committedState,
        blockers.Append(CharacterCreationSkillsBlockers.PostCommitRefreshRequired)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(blocker => blocker, StringComparer.Ordinal)
            .ToArray());

    private static bool ReceiptMatchesState(
        CharacterCreationSkillsReceipt receipt,
        CharacterCreationSkillsPreview preview,
        CharacterCreationSkillsState refreshed,
        string idempotencyKey) =>
        refreshed.PendingDraft is { } draft
        && receipt.WorkspaceId == preview.Binding.WorkspaceId
        && receipt.WorkspaceId == refreshed.Binding.WorkspaceId
        && receipt.PreviousContentRevision == preview.Binding.ContentRevision
        && receipt.ContentRevision == receipt.PreviousContentRevision + 1
        && receipt.ContentRevision == refreshed.Binding.ContentRevision
        && receipt.SavedRevision == refreshed.Binding.SavedRevision
        && receipt.DraftRevision == draft.DraftRevision
        && Equal(receipt.DraftDigest, draft.DraftDigest)
        && Equal(receipt.PreviewDigest, preview.PreviewDigest)
        && Equal(receipt.IdempotencyKeyDigest, CharacterCreationSkillsDigest.ComputeUtf8(idempotencyKey))
        && Equal(receipt.SkillsAuthorityDigest, draft.SkillsAuthorityDigest)
        && Equal(receipt.RuntimeDigest, draft.RuntimeDigest)
        && receipt.ActivePointsRemaining == (int)preview.ActiveSkillPointBudget.Remaining
        && receipt.SkillGroupPointsRemaining == (int)preview.SkillGroupPointBudget.Remaining
        && receipt.KnowledgePointsRemaining == (int)preview.KnowledgeSkillPointBudget.Remaining
        && receipt.KnowledgePointOverflowToActive == preview.KnowledgePointOverflowToActive
        && draft.ActivePointTotal == (int)preview.ActiveSkillPointBudget.Total
        && draft.ActivePointUsed == (int)preview.ActiveSkillPointBudget.Used
        && draft.SkillGroupPointTotal == (int)preview.SkillGroupPointBudget.Total
        && draft.SkillGroupPointUsed == (int)preview.SkillGroupPointBudget.Used
        && draft.KnowledgePointTotal == (int)preview.KnowledgeSkillPointBudget.Total
        && draft.KnowledgePointUsed == (int)preview.KnowledgeSkillPointBudget.Used
        && CharacterCreationSkillsDigest.IsValidReceipt(receipt, receipt.WorkspaceId, receipt.ContentRevision)
        && !receipt.CharacterDocumentChanged;

    private static bool AllocationsEqual(
        IReadOnlyList<CharacterCreationSkillProjection> projection,
        IReadOnlyList<CharacterCreationSkillAllocation> allocations) =>
        CharacterCreationSkillsDigest.EqualsFixedTime(
            CharacterCreationSkillsDigest.Compute(projection.OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.SourceSkillId, StringComparer.Ordinal)
                .Select(item => new CharacterCreationSkillAllocation(item.SourceSkillId, item.Kind, item.Rating,
                    item.SpecializationOptionId, item.IsNativeLanguage)).ToArray()),
            CharacterCreationSkillsDigest.Compute(allocations.OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.SourceSkillId, StringComparer.Ordinal).ToArray()));

    private static bool GroupsEqual(
        IReadOnlyList<CharacterCreationSkillGroupProjection> projection,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups) =>
        CharacterCreationSkillsDigest.EqualsFixedTime(
            CharacterCreationSkillsDigest.Compute(projection.OrderBy(item => item.GroupId, StringComparer.Ordinal)
                .Select(item => new CharacterCreationSkillGroupAllocation(item.GroupId, item.Rating)).ToArray()),
            CharacterCreationSkillsDigest.Compute(groups.OrderBy(item => item.GroupId, StringComparer.Ordinal).ToArray()));

    private static bool Digests(CharacterCreationSkillsBinding binding) =>
        CharacterCreationSkillsDigest.IsCanonical(binding.RawCharacterXmlDigest)
        && binding.AuxiliaryStateDigest is { Length: 64 }
        && CharacterCreationSkillsDigest.IsCanonical(binding.PrerequisiteDraftDigest)
        && CharacterCreationSkillsDigest.IsCanonical(binding.PrerequisiteAuthorityDigest)
        && CharacterCreationSkillsDigest.IsCanonical(binding.AttributesDraftDigest)
        && CharacterCreationSkillsDigest.IsCanonical(binding.SkillsAuthorityDigest)
        && CharacterCreationSkillsDigest.IsCanonical(binding.RuntimeDigest)
        && CharacterCreationSkillsDigest.IsCanonical(binding.ContributionInputsDigest);

    private static bool Exact(CharacterCreationBudgetState budget) =>
        budget.IsExact && budget.Blockers.Count == 0 && budget.Total >= 0 && budget.Used >= 0
        && budget.Remaining >= 0 && budget.Used + budget.Remaining == budget.Total;
    private static bool Equal(string left, string right) => CharacterCreationSkillsDigest.EqualsFixedTime(left, right);
}
