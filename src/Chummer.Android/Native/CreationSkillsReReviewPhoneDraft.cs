using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>A separate, read-only proposal lane. A blocked historical Skills
/// snapshot is never relabelled as an editable current snapshot.</summary>
internal sealed class CreationSkillsReReviewPhoneDraft
{
    public CharacterCreationSkillsReReviewState? State { get; private set; }
    public CharacterCreationSkillsReReviewPreview? Preview { get; private set; }
    public IReadOnlyList<CharacterCreationSkillAllocation> Skills { get; private set; } = [];
    public IReadOnlyList<CharacterCreationSkillGroupAllocation> Groups { get; private set; } = [];

    public bool Bind(CharacterCreationSkillsReReviewState state, CharacterOverviewState overview)
    {
        if (!CreationSkillsReReviewPhoneAuthority.Matches(state, overview)) return false;
        State = state;
        Preview = state.InitialPreview;
        Skills = state.HistoricalDraft.Allocations.ToArray();
        Groups = state.HistoricalDraft.GroupAllocations.ToArray();
        return true;
    }

    public bool TryAdopt(CharacterOverviewState overview,
        CharacterCreationFoundationResult<CharacterCreationSkillsReReviewPreview> result,
        IReadOnlyList<CharacterCreationSkillAllocation> skills,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups)
    {
        if (State is null || !CreationSkillsReReviewPhoneAuthority.Matches(State, overview)
            || result.Outcome is not (CharacterCreationFoundationOutcomes.Success or CharacterCreationFoundationOutcomes.Blocked)
            || result.Value is not { } preview
            || !CreationSkillsReReviewPhoneAuthority.ValidPreview(State, preview, skills, groups)) return false;
        // Blocked intermediate corrections are useful proposals, not permission
        // to save. Retain exact requested identities, never silently delete rows.
        Preview = preview;
        Skills = skills.ToArray();
        Groups = groups.ToArray();
        return true;
    }

    public bool CanConfirm(CharacterOverviewState overview) => State is not null && Preview is not null
        && CreationSkillsReReviewPhoneAuthority.Matches(State, overview)
        && CreationSkillsReReviewPhoneAuthority.ValidPreview(State, Preview, Skills, Groups)
        && Preview.CurrentPreview.CanConfirm && Preview.CurrentPreview.Blockers.Count == 0;
}

internal static class CreationSkillsReReviewPhoneAuthority
{
    public static bool Equal<T>(T left, T right) => CharacterCreationSkillsDigest.EqualsFixedTime(
        CharacterCreationSkillsDigest.Compute(left), CharacterCreationSkillsDigest.Compute(right));

    public static bool Matches(CharacterCreationSkillsReReviewState state, CharacterOverviewState overview) =>
        state.Schema == CharacterCreationSkillsReReviewSchemas.SnapshotV1
        && overview.Profile?.Created == false
        && overview.WorkspaceId == state.Binding.Current.WorkspaceId
        && overview.ContentRevision == state.Binding.Current.ContentRevision
        && overview.SavedRevision == state.Binding.Current.SavedRevision
        && state.Binding.Schema == CharacterCreationSkillsReReviewSchemas.BindingV1
        && Digest(state.Binding.ContextDigest, state.Binding with { ContextDigest = string.Empty })
        && Digest(state.SnapshotDigest, state with { SnapshotDigest = string.Empty })
        && CharacterCreationSkillsDigest.EqualsFixedTime(state.Binding.HistoricalDraftDigest, state.HistoricalDraft.DraftDigest)
        && CharacterCreationSkillsDigest.IsCanonical(state.Binding.HistoricalReceiptDigest)
        && CharacterCreationSkillsDigest.EqualsFixedTime(state.HistoricalDraft.DraftDigest,
            CharacterCreationSkillsDraftIntegrity.ComputeDigest(state.HistoricalDraft))
        && Equal(state.CurrentState.PendingDraft, state.HistoricalDraft)
        && Equal(state.CurrentState.Binding, state.Binding.Current)
        && !state.CurrentState.CanEdit
        && state.CurrentState.Blockers.Contains(CharacterCreationSkillsBlockers.DraftInvalid)
        && CharacterCreationSkillsDraftIntegrity.IsValidAuthority(state.CurrentState.Authority)
        && Digest(state.CurrentState.SnapshotDigest, state.CurrentState with { SnapshotDigest = string.Empty })
        && ValidPreview(state, state.InitialPreview, state.HistoricalDraft.Allocations, state.HistoricalDraft.GroupAllocations);

    public static bool ValidPreview(CharacterCreationSkillsReReviewState state,
        CharacterCreationSkillsReReviewPreview preview,
        IReadOnlyList<CharacterCreationSkillAllocation> skills,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups) =>
        preview.Schema == CharacterCreationSkillsReReviewSchemas.PreviewV1
        && Equal(state.Binding, preview.Binding)
        && Equal(preview.Binding.Current, preview.CurrentPreview.Binding)
        && preview.CurrentPreview.Schema == CharacterCreationSkillsSchemas.PreviewV1
        && preview.CurrentPreview.RequiresExplicitConfirmation
        && Digest(preview.PreviewDigest, preview with { PreviewDigest = string.Empty })
        && Digest(preview.CurrentPreview.PreviewDigest, preview.CurrentPreview with { PreviewDigest = string.Empty })
        && Equal(preview.CurrentPreview.Skills.Select(row => new CharacterCreationSkillAllocation(
                row.SourceSkillId, row.Kind, row.Rating, row.SpecializationOptionId, row.IsNativeLanguage))
            .OrderBy(row => row.Kind, StringComparer.Ordinal).ThenBy(row => row.SourceSkillId, StringComparer.Ordinal).ToArray(),
            skills.OrderBy(row => row.Kind, StringComparer.Ordinal).ThenBy(row => row.SourceSkillId, StringComparer.Ordinal).ToArray())
        && Equal(preview.CurrentPreview.SkillGroups.Select(row => new CharacterCreationSkillGroupAllocation(row.GroupId, row.Rating))
            .OrderBy(row => row.GroupId, StringComparer.Ordinal).ToArray(),
            groups.OrderBy(row => row.GroupId, StringComparer.Ordinal).ToArray());

    public static string IdempotencyKey(CharacterCreationSkillsReReviewPreview preview) =>
        CharacterCreationSkillsDigest.Compute(new
        {
            Schema = "chummer.android.creation-skills-rereview-idempotency.v1",
            preview.Binding, preview.PreviewDigest
        });

    public static bool ReceiptMatches(CharacterCreationSkillsReceipt receipt,
        CharacterCreationSkillsReReviewPreview preview, CharacterCreationSkillsState current, string key) =>
        current.CanEdit && current.Blockers.Count == 0
        && CharacterCreationSkillsDraftIntegrity.IsValidStateProjection(current)
        && Digest(current.SnapshotDigest, current with { SnapshotDigest = string.Empty })
        && current.PendingDraft is { } draft
        && receipt.WorkspaceId == preview.Binding.Current.WorkspaceId
        && current.Binding.WorkspaceId == receipt.WorkspaceId
        && receipt.PreviousContentRevision == preview.Binding.Current.ContentRevision
        && receipt.ContentRevision == receipt.PreviousContentRevision + 1
        && current.Binding.ContentRevision == receipt.ContentRevision
        && current.Binding.SavedRevision == receipt.SavedRevision
        && receipt.DraftRevision == draft.DraftRevision
        && receipt.PreviousReceiptDigest == preview.Binding.HistoricalReceiptDigest
        && receipt.DraftDigest == draft.DraftDigest
        && Equal(draft.Skills, preview.CurrentPreview.Skills)
        && Equal(draft.SkillGroups, preview.CurrentPreview.SkillGroups)
        && current.Binding.RawCharacterXmlDigest == preview.Binding.Current.RawCharacterXmlDigest
        && receipt.PreviewDigest == preview.PreviewDigest
        && draft.LastPreviewDigest == preview.PreviewDigest
        && receipt.CommandDigest == draft.LastCommandDigest
        && receipt.SkillsAuthorityDigest == preview.Binding.Current.SkillsAuthorityDigest
        && receipt.RuntimeDigest == preview.Binding.Current.RuntimeDigest
        && receipt.IdempotencyKeyDigest == CharacterCreationSkillsDigest.ComputeUtf8(key)
        && receipt.ActivePointsRemaining == preview.CurrentPreview.ActiveSkillPointBudget.Remaining
        && receipt.SkillGroupPointsRemaining == preview.CurrentPreview.SkillGroupPointBudget.Remaining
        && receipt.KnowledgePointsRemaining == preview.CurrentPreview.KnowledgeSkillPointBudget.Remaining
        && receipt.KnowledgePointOverflowToActive == preview.CurrentPreview.KnowledgePointOverflowToActive
        && CharacterCreationSkillsDigest.IsValidReceipt(receipt, receipt.WorkspaceId, receipt.ContentRevision)
        && !receipt.CharacterDocumentChanged;

    private static bool Digest<T>(string digest, T value) => CharacterCreationSkillsDigest.IsCanonical(digest)
        && CharacterCreationSkillsDigest.EqualsFixedTime(digest, CharacterCreationSkillsDigest.Compute(value));
}
