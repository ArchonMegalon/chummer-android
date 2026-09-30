using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

internal sealed record CreationMagicReReviewConfirmResult(string Outcome,
    CharacterCreationMagicResonanceReceipt? Receipt, IReadOnlyList<string> Blockers);

// Identity checks only. Core alone decides whether unchanged saved selections
// can be reviewed against the current Attributes draft and commits the decision.
internal static class CreationMagicReReviewPhoneAuthority
{
    public static bool Equal<T>(T left, T right) => CharacterCreationMagicResonanceDigest.EqualsFixedTime(
        CharacterCreationMagicResonanceDigest.Compute(left), CharacterCreationMagicResonanceDigest.Compute(right));

    public static bool Matches(CharacterCreationMagicResonanceReReviewState state, CharacterOverviewState overview) =>
        state.Schema == CharacterCreationMagicResonanceReReviewSchemas.SnapshotV1
        && overview.Profile?.Created == false && !overview.IsDirty && string.IsNullOrWhiteSpace(overview.Error)
        && overview.WorkspaceId == state.Binding.Current.WorkspaceId
        && overview.ContentRevision == state.Binding.Current.ContentRevision
        && overview.SavedRevision == state.Binding.Current.SavedRevision
        && state.Binding.Schema == CharacterCreationMagicResonanceReReviewSchemas.BindingV1
        && Digest(state.Binding.ContextDigest, state.Binding with { ContextDigest = string.Empty })
        && Digest(state.SnapshotDigest, state with { SnapshotDigest = string.Empty })
        && state.Binding.HistoricalDraftDigest == state.HistoricalDraft.DraftDigest
        && CharacterCreationMagicResonanceDigest.IsCanonical(state.Binding.HistoricalReceiptDigest)
        && Digest(state.HistoricalDraft.DraftDigest, state.HistoricalDraft with { DraftDigest = string.Empty })
        && Equal(state.CurrentState.PendingDraft, state.HistoricalDraft)
        && Equal(state.CurrentState.Binding, state.Binding.Current)
        && !state.CurrentState.CanEdit
        && state.CurrentState.Blockers.Contains(CharacterCreationMagicResonanceBlockers.DraftInvalid)
        && CharacterCreationMagicResonanceDraftIntegrity.IsValidAuthority(state.CurrentState.Authority)
        && Digest(state.CurrentState.SnapshotDigest, state.CurrentState with { SnapshotDigest = string.Empty })
        && ValidPreview(state, state.InitialPreview);

    public static bool ValidPreview(CharacterCreationMagicResonanceReReviewState state,
        CharacterCreationMagicResonanceReReviewPreview preview) =>
        preview.Schema == CharacterCreationMagicResonanceReReviewSchemas.PreviewV1
        && Equal(state.Binding, preview.Binding)
        && Equal(preview.Binding.Current, preview.CurrentPreview.Binding)
        && preview.CurrentPreview.Schema == CharacterCreationMagicResonanceSchemas.PreviewV1
        && preview.CurrentPreview.RequiresExplicitConfirmation
        && preview.CurrentPreview.CanConfirm && preview.CurrentPreview.Blockers.Count == 0
        && Equal(preview.CurrentPreview.Selections, state.HistoricalDraft.Selections)
        && Digest(preview.PreviewDigest, preview with { PreviewDigest = string.Empty })
        && Digest(preview.CurrentPreview.PreviewDigest, preview.CurrentPreview with { PreviewDigest = string.Empty });

    public static string IdempotencyKey(CharacterCreationMagicResonanceReReviewPreview preview) =>
        CharacterCreationMagicResonanceDigest.Compute(new
        {
            Schema = "chummer.android.creation-magic-rereview-idempotency.v1",
            preview.Binding, preview.PreviewDigest
        });

    public static bool ReceiptMatches(CharacterCreationMagicResonanceReceipt receipt,
        CharacterCreationMagicResonanceReReviewPreview preview, string key) =>
        receipt.WorkspaceId == preview.Binding.Current.WorkspaceId
        && receipt.PreviousContentRevision == preview.Binding.Current.ContentRevision
        && receipt.ContentRevision == receipt.PreviousContentRevision + 1
        && receipt.SavedRevision == receipt.ContentRevision
        && receipt.PreviousReceiptDigest == preview.Binding.HistoricalReceiptDigest
        && receipt.PreviewDigest == preview.PreviewDigest
        && receipt.AuthorityDigest == preview.Binding.Current.AuthorityDigest
        && receipt.SourceInputsDigest == preview.Binding.Current.SourceInputsDigest
        && receipt.CustomDataInputsDigest == preview.Binding.Current.CustomDataInputsDigest
        && receipt.GmPolicyDigest == preview.Binding.Current.GmPolicyDigest
        && receipt.RuntimeDigest == preview.Binding.Current.RuntimeDigest
        && receipt.IdempotencyKeyDigest == CharacterCreationMagicResonanceDigest.ComputeUtf8(key)
        && CharacterCreationMagicResonanceDigest.IsValidReceipt(receipt, receipt.WorkspaceId, receipt.ContentRevision)
        && !receipt.CharacterDocumentChanged;

    public static bool SavedMatches(CharacterCreationMagicResonanceReceipt receipt,
        CharacterCreationMagicResonanceReReviewPreview preview, CharacterCreationMagicResonanceState current,
        string key) => ReceiptMatches(receipt, preview, key)
        && current.CanEdit && current.Blockers.Count == 0
        && CharacterCreationMagicResonanceWorkflow.TryProject(current, out _)
        && current.PendingDraft is { } draft
        && current.Binding.WorkspaceId == receipt.WorkspaceId
        && current.Binding.ContentRevision == receipt.ContentRevision && current.Binding.SavedRevision == receipt.SavedRevision
        && receipt.DraftRevision == draft.DraftRevision && receipt.DraftDigest == draft.DraftDigest
        && draft.LastPreviewDigest == preview.PreviewDigest && draft.LastCommandDigest == receipt.CommandDigest
        && Equal(draft.Selections, preview.CurrentPreview.Selections)
        && Equal(draft.FinalizationContribution, preview.CurrentPreview.FinalizationContribution)
        && current.Binding.RawCharacterXmlDigest == preview.Binding.Current.RawCharacterXmlDigest;

    private static bool Digest<T>(string digest, T value) => CharacterCreationMagicResonanceDigest.IsCanonical(digest)
        && CharacterCreationMagicResonanceDigest.EqualsFixedTime(digest, CharacterCreationMagicResonanceDigest.Compute(value));
}
