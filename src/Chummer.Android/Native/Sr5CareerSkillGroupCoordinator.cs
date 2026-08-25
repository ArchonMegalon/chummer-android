using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public interface ISr5CareerSkillGroupPresenter
{
    Sr5CareerRunnerBinding Binding { get; }

    Task<CareerSkillGroupAdvanceEditorState?> LoadSkillGroupsAsync(
        CancellationToken cancellationToken);

    Task<bool> ApplyAndSaveAsync(
        CareerSkillGroupAdvanceRequest request,
        CancellationToken cancellationToken);

    Task<CharacterCareerSkillGroupCorrectionPlan?> CorrectAndSaveAsync(
        CareerSkillGroupCorrectionRequest request,
        CancellationToken cancellationToken);
}
internal sealed class RunnerSessionSr5CareerSkillGroupPresenter(
    RunnerSessionCoordinator coordinator) : ISr5CareerSkillGroupPresenter
{
    public Sr5CareerRunnerBinding Binding => new(
        coordinator.State.Profile?.Created == true,
        coordinator.State.Rules?.GameEdition,
        coordinator.State.WorkspaceId,
        coordinator.State.ContentRevision,
        coordinator.State.SavedRevision,
        coordinator.State.IsDirty,
        coordinator.State.Error);

    public Task<CareerSkillGroupAdvanceEditorState?> LoadSkillGroupsAsync(
        CancellationToken cancellationToken)
        => coordinator.PrepareCareerSkillGroupAdvanceAsync(cancellationToken);

    public Task<bool> ApplyAndSaveAsync(
        CareerSkillGroupAdvanceRequest request,
        CancellationToken cancellationToken)
        => coordinator.ApplyCareerSkillGroupAdvanceAsync(request, cancellationToken);

    public Task<CharacterCareerSkillGroupCorrectionPlan?> CorrectAndSaveAsync(
        CareerSkillGroupCorrectionRequest request,
        CancellationToken cancellationToken)
        => coordinator.CorrectCareerSkillGroupAdvanceAsync(request, cancellationToken);
}

/// <summary>
/// Public SR5 skill-group action boundary. It accepts only a reviewed typed Core
/// plan and verifies the post-save result from a fresh Presentation projection.
/// </summary>
public sealed class Sr5CareerSkillGroupCoordinator(
    ISr5CareerSkillGroupPresenter presenter,
    ISr5CareerCheckpointOwnerAuthority ownerAuthority)
{
    public async Task<CareerSkillGroupAdvanceEditorState?> PrepareAsync(
        CancellationToken cancellationToken = default)
    {
        Sr5CareerActiveSkillCoordinator.RequireCreatedSr5(presenter.Binding);
        CareerSkillGroupAdvanceEditorState? editor =
            await presenter.LoadSkillGroupsAsync(cancellationToken).ConfigureAwait(false);
        Sr5CareerRunnerBinding after = presenter.Binding;
        Sr5CareerActiveSkillCoordinator.RequireCreatedSr5(after);
        if (editor is not null
            && (after.WorkspaceId != editor.WorkspaceId
                || after.ContentRevision != editor.ContentRevision))
        {
            throw new InvalidOperationException(
                "The SR5 runner changed while its skill groups were being loaded.");
        }
        return editor;
    }

    public async Task<Sr5CareerSkillGroupApplyResult> ApplyAsync(
        Sr5CareerSkillGroupDraft draft,
        Sr5CareerSkillGroupCheckpoint applyingCheckpoint,
        Sr5CareerSkillGroupCheckpointStore checkpointStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(applyingCheckpoint);
        ArgumentNullException.ThrowIfNull(checkpointStore);
        using IDisposable applyingLease =
            await checkpointStore.AcquireDurableApplyingLeaseAsync(
                applyingCheckpoint,
                cancellationToken).ConfigureAwait(false);
        Sr5CareerRunnerBinding before = presenter.Binding;
        Sr5CareerActiveSkillCoordinator.RequireCreatedSr5(before);
        if (ownerAuthority.CurrentOwnerId != draft.OwnerId
            || before.WorkspaceId != draft.WorkspaceId
            || before.ContentRevision != draft.ExpectedContentRevision
            || before.SavedRevision != draft.ExpectedContentRevision
            || before.IsDirty
            || !string.IsNullOrWhiteSpace(before.Error)
            || !draft.IsExact())
        {
            throw new InvalidOperationException(
                "The reviewed SR5 skill-group action does not own the current runner revision.");
        }
        if (!applyingCheckpoint.MatchesActionDraft(draft))
        {
            throw new InvalidOperationException(
                "The exact durable Applying checkpoint does not own this skill-group action.");
        }

        _ = await presenter.ApplyAndSaveAsync(draft.ToRequest(), cancellationToken)
            .ConfigureAwait(false);
        Sr5CareerSkillGroupRecoveryResolution resolution = await ResolveAsync(
            applyingCheckpoint,
            cancellationToken).ConfigureAwait(false);
        return resolution.Status switch
        {
            Sr5CareerSkillGroupRecoveryStatus.AppliedVerified when resolution.Receipt is { } receipt =>
                new Sr5CareerSkillGroupApplyResult(
                    Sr5CareerSkillGroupApplyStatus.Applied,
                    draft.ActionPlan,
                    checked(draft.ExpectedContentRevision + 1),
                    receipt,
                    resolution,
                    resolution.Message),
            Sr5CareerSkillGroupRecoveryStatus.NotAppliedVerified =>
                new Sr5CareerSkillGroupApplyResult(
                    Sr5CareerSkillGroupApplyStatus.RejectedBeforeMutation,
                    draft.ActionPlan,
                    SavedContentRevision: null,
                    Receipt: null,
                    Resolution: resolution,
                    Message: resolution.Message),
            _ => new Sr5CareerSkillGroupApplyResult(
                Sr5CareerSkillGroupApplyStatus.OutcomeUnknown,
                draft.ActionPlan,
                SavedContentRevision: null,
                Receipt: null,
                Resolution: resolution,
                Message: resolution.Message)
        };
    }

    public async Task<Sr5CareerSkillGroupRecoveryResolution> ResolveAsync(
        Sr5CareerSkillGroupCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Sr5CareerRunnerBinding before = presenter.Binding;
        Sr5CareerActiveSkillCoordinator.RequireCreatedSr5(before);
        if (!checkpoint.IsStructurallyValid()
            || ownerAuthority.CurrentOwnerId == Guid.Empty
            || ownerAuthority.CurrentOwnerId != checkpoint.Draft.OwnerId
            || before.WorkspaceId != checkpoint.Draft.WorkspaceId)
        {
            throw new InvalidOperationException(
                "The skill-group recovery checkpoint does not belong to the authenticated local SR5 owner and runner.");
        }

        CareerSkillGroupAdvanceEditorState? editor =
            await presenter.LoadSkillGroupsAsync(cancellationToken).ConfigureAwait(false);
        Sr5CareerRunnerBinding after = presenter.Binding;
        Sr5CareerActiveSkillCoordinator.RequireCreatedSr5(after);
        if (before.WorkspaceId != after.WorkspaceId
            || before.ContentRevision != after.ContentRevision
            || before.SavedRevision != after.SavedRevision)
        {
            return Unknown(checkpoint, "The runner changed during authoritative skill-group outcome lookup.");
        }

        return Resolve(checkpoint, after, editor);
    }

    public async Task<CharacterCareerSkillGroupCorrectionPlan> CorrectAsync(
        Sr5CareerSkillGroupCheckpoint checkpoint,
        CharacterCareerSkillGroupAdvanceReceipt receipt,
        Guid correctionId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(receipt);
        Sr5CareerRunnerBinding before = presenter.Binding;
        Sr5CareerActiveSkillCoordinator.RequireCreatedSr5(before);
        long expectedRevision = checked(checkpoint.Draft.ExpectedContentRevision + 1);
        if (!checkpoint.IsStructurallyValid()
            || checkpoint.Phase != Sr5CareerCheckpointPhase.Applied
            || !ReceiptMatchesDraft(checkpoint.Draft, receipt)
            || ownerAuthority.CurrentOwnerId != checkpoint.Draft.OwnerId
            || before.WorkspaceId != checkpoint.Draft.WorkspaceId
            || before.ContentRevision != expectedRevision
            || before.SavedRevision != expectedRevision
            || before.IsDirty
            || !string.IsNullOrWhiteSpace(before.Error)
            || correctionId == Guid.Empty
            || correctionId == receipt.TransactionId
            || string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException(
                "The compensating skill-group correction does not own the exact saved receipt revision.");
        }

        CareerSkillGroupCorrectionRequest request = new(
            checkpoint.Draft.WorkspaceId,
            expectedRevision,
            CharacterCareerSkillGroupAdvanceRules.RulesetId,
            receipt,
            receipt.ReceiptDigest,
            Confirmed: true,
            correctionId,
            reason);
        CharacterCareerSkillGroupCorrectionPlan? correction =
            await presenter.CorrectAndSaveAsync(request, cancellationToken).ConfigureAwait(false);
        Sr5CareerRunnerBinding after = presenter.Binding;
        if (!CharacterCareerSkillGroupAdvanceRules.IsCoherent(correction)
            || correction!.CorrectionId != correctionId
            || correction.OriginalTransactionId != receipt.TransactionId
            || correction.ExpenseIdToRemove != receipt.ExpenseId
            || correction.Identity != receipt.Identity
            || correction.SavedGroupKarmaPoints != receipt.GroupKarmaBefore
            || correction.SavedCharacterKarma != receipt.CharacterKarmaBefore
            || correction.RestoredGroupRating != receipt.GroupRatingBefore
            || correction.RestoredCostRating != receipt.CostRatingBefore
            || !string.Equals(
                correction.ExpectedPostLogicalRevision,
                receipt.LogicalRevisionAfter,
                StringComparison.Ordinal)
            || !string.Equals(
                correction.ExpectedPostSourceRevision,
                receipt.SourceRevisionAfter,
                StringComparison.Ordinal)
            || !string.Equals(
                correction.ExpectedPostRuleDigest,
                receipt.RuleDigestAfter,
                StringComparison.Ordinal)
            || !string.Equals(
                correction.OriginalReceiptDigest,
                receipt.ReceiptDigest,
                StringComparison.Ordinal)
            || after.WorkspaceId != checkpoint.Draft.WorkspaceId
            || after.ContentRevision != expectedRevision + 1
            || after.SavedRevision != after.ContentRevision
            || after.IsDirty
            || !string.IsNullOrWhiteSpace(after.Error))
        {
            throw new InvalidOperationException(
                "The compensating skill-group correction was not verified from one exact clean saved revision.");
        }

        return correction;
    }

    internal static Sr5CareerSkillGroupRecoveryResolution Resolve(
        Sr5CareerSkillGroupCheckpoint checkpoint,
        Sr5CareerRunnerBinding binding,
        CareerSkillGroupAdvanceEditorState? editor)
    {
        if (!checkpoint.IsStructurallyValid()
            || binding.WorkspaceId != checkpoint.Draft.WorkspaceId
            || editor is null
            || editor.WorkspaceId != checkpoint.Draft.WorkspaceId
            || editor.ContentRevision != binding.ContentRevision
            || binding.IsDirty
            || !string.IsNullOrWhiteSpace(binding.Error))
        {
            return Unknown(
                checkpoint,
                "A fresh clean typed skill-group projection was unavailable for one saved revision.");
        }

        CharacterCareerSkillGroupAdvanceReceipt[] matchingReceipts = editor.RecoverableReceipts
            .Where(candidate => candidate.TransactionId == checkpoint.Draft.Plan.TransactionId)
            .Take(2)
            .ToArray();
        bool appliedRevision = checkpoint.Draft.ExpectedContentRevision < long.MaxValue
            && binding.ContentRevision == checkpoint.Draft.ExpectedContentRevision + 1
            && binding.SavedRevision == binding.ContentRevision;
        if (appliedRevision
            && matchingReceipts.Length == 1
            && ReceiptMatchesDraft(checkpoint.Draft, matchingReceipts[0]))
        {
            return Sr5CareerSkillGroupRecoveryProof.Create(
                checkpoint,
                Sr5CareerSkillGroupRecoveryStatus.AppliedVerified,
                matchingReceipts[0],
                "Fresh typed skill-group and persisted receipt projections verify the exact saved advancement.");
        }

        CharacterCareerSkillGroupAdvanceQuote[] matchingQuotes = editor.SkillGroups
            .Where(candidate => candidate.Identity == checkpoint.Draft.Quote.Identity)
            .Take(2)
            .ToArray();
        bool originalQuoteStillExact = matchingQuotes.Length == 1
            && CharacterCareerSkillGroupAdvanceRules.IsCoherent(matchingQuotes[0])
            && string.Equals(
                matchingQuotes[0].LogicalRevision,
                checkpoint.Draft.Quote.LogicalRevision,
                StringComparison.Ordinal)
            && string.Equals(
                matchingQuotes[0].SourceRevision,
                checkpoint.Draft.Quote.SourceRevision,
                StringComparison.Ordinal)
            && string.Equals(
                matchingQuotes[0].RuleDigest,
                checkpoint.Draft.Quote.RuleDigest,
                StringComparison.Ordinal);
        bool notAppliedRevision = binding.ContentRevision == checkpoint.Draft.ExpectedContentRevision
            && binding.SavedRevision == checkpoint.Draft.ExpectedContentRevision;
        if (notAppliedRevision
            && matchingReceipts.Length == 0
            && originalQuoteStillExact)
        {
            return Sr5CareerSkillGroupRecoveryProof.Create(
                checkpoint,
                Sr5CareerSkillGroupRecoveryStatus.NotAppliedVerified,
                receipt: null,
                "Fresh typed projections prove that neither the skill group nor its receipt transaction was saved.");
        }

        return Unknown(
            checkpoint,
            "The authoritative skill-group state is partial or conflicts with the reviewed action. Do not replay or clear it.");
    }

    internal static bool ReceiptMatchesDraft(
        Sr5CareerSkillGroupDraft draft,
        CharacterCareerSkillGroupAdvanceReceipt receipt)
        => draft.IsExact()
            && CharacterCareerSkillGroupAdvanceRules.IsCoherent(receipt)
            && receipt.TransactionId == draft.Plan.TransactionId
            && receipt.ExpenseId == draft.Plan.ExpenseId
            && receipt.Identity == draft.Quote.Identity
            && receipt.GroupKarmaBefore == draft.Quote.KarmaPoints
            && receipt.GroupKarmaAfter == draft.Plan.SavedGroupKarmaPoints
            && receipt.CharacterKarmaBefore == draft.Quote.AvailableKarma
            && receipt.CharacterKarmaAfter == draft.Plan.SavedCharacterKarma
            && receipt.GroupRatingBefore == draft.Quote.GroupRating
            && receipt.GroupRatingAfter == draft.Plan.TargetGroupRating
            && receipt.CostRatingBefore == draft.Quote.CostRating
            && receipt.CostRatingAfter == draft.Plan.TargetCostRating
            && receipt.EnabledMemberCount == draft.Quote.EnabledMemberCount
            && receipt.ExpenseAmount == draft.Plan.ExpenseAmount
            && string.Equals(receipt.ExpenseReason, draft.Plan.ExpenseReason, StringComparison.Ordinal)
            && string.Equals(receipt.LogicalRevisionBefore, draft.Quote.LogicalRevision, StringComparison.Ordinal)
            && string.Equals(receipt.SourceRevisionBefore, draft.Quote.SourceRevision, StringComparison.Ordinal)
            && string.Equals(receipt.RuleDigestBefore, draft.Quote.RuleDigest, StringComparison.Ordinal);

    private static Sr5CareerSkillGroupRecoveryResolution Unknown(
        Sr5CareerSkillGroupCheckpoint checkpoint,
        string message)
        => Sr5CareerSkillGroupRecoveryProof.Create(
            checkpoint,
            Sr5CareerSkillGroupRecoveryStatus.OutcomeUnknown,
            receipt: null,
            message);
}

internal static class Sr5CareerSkillGroupRecoveryProof
{
    // A resolution is verified and consumed in one process. After process death
    // the coordinator must query the authoritative presenter again and mint a
    // new proof; no proof is persisted as checkpoint authority.
    private static readonly byte[] ProcessKey = RandomNumberGenerator.GetBytes(32);

    public static Sr5CareerSkillGroupRecoveryResolution Create(
        Sr5CareerSkillGroupCheckpoint checkpoint,
        Sr5CareerSkillGroupRecoveryStatus status,
        CharacterCareerSkillGroupAdvanceReceipt? receipt,
        string message)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return new(
            status,
            checkpoint.Draft.WorkspaceId.Value,
            checkpoint.Draft.OwnerId,
            checkpoint.Draft.Plan.TransactionId,
            checkpoint.Version,
            receipt,
            message,
            Sign(checkpoint, status, receipt, message));
    }

    public static bool Verifies(
        Sr5CareerSkillGroupCheckpoint checkpoint,
        Sr5CareerSkillGroupRecoveryResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(resolution);
        if (resolution.AuthorityProof is not { Length: 64 })
        {
            return false;
        }
        try
        {
            byte[] actual = Convert.FromHexString(resolution.AuthorityProof);
            byte[] expected = Convert.FromHexString(Sign(
                checkpoint,
                resolution.Status,
                resolution.Receipt,
                resolution.Message));
            return actual.Length == expected.Length
                && CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Sign(
        Sr5CareerSkillGroupCheckpoint checkpoint,
        Sr5CareerSkillGroupRecoveryStatus status,
        CharacterCareerSkillGroupAdvanceReceipt? receipt,
        string message)
    {
        string payload = string.Join(
            "\n",
            JsonSerializer.Serialize(checkpoint),
            status.ToString(),
            JsonSerializer.Serialize(receipt),
            message);
        return Convert.ToHexString(
            HMACSHA256.HashData(ProcessKey, Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }
}
