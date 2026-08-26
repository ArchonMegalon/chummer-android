using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public interface ISr5CareerQualityPresenter
{
    Sr5CareerRunnerBinding Binding { get; }

    Task<CareerQualityEditorState?> LoadAsync(CancellationToken cancellationToken);

    Task<CareerQualityReview> ReviewAsync(
        CareerQualityDraft draft,
        CancellationToken cancellationToken);

    Task<CareerQualityConfirmation> ConfirmAndRefreshAsync(
        CareerQualityReview review,
        Guid transactionId,
        DateTime expenseDateLocal,
        CancellationToken cancellationToken);

    Task<CareerQualityCorrectionConfirmation> CorrectAndRefreshAsync(
        CareerQualityCorrectionRequest request,
        CancellationToken cancellationToken);
}

internal sealed class RunnerSessionSr5CareerQualityPresenter(
    RunnerSessionCoordinator coordinator) : ISr5CareerQualityPresenter
{
    public Sr5CareerRunnerBinding Binding => new(
        coordinator.State.Profile?.Created == true,
        coordinator.State.Rules?.GameEdition,
        coordinator.State.WorkspaceId,
        coordinator.State.ContentRevision,
        coordinator.State.SavedRevision,
        coordinator.State.IsDirty,
        coordinator.State.Error);

    public Task<CareerQualityEditorState?> LoadAsync(CancellationToken cancellationToken)
        => coordinator.PrepareCareerQualityAsync(cancellationToken);

    public Task<CareerQualityReview> ReviewAsync(
        CareerQualityDraft draft,
        CancellationToken cancellationToken)
        => coordinator.ReviewCareerQualityAsync(draft, cancellationToken);

    public Task<CareerQualityConfirmation> ConfirmAndRefreshAsync(
        CareerQualityReview review,
        Guid transactionId,
        DateTime expenseDateLocal,
        CancellationToken cancellationToken)
        => coordinator.ConfirmCareerQualityAsync(
            review,
            transactionId,
            expenseDateLocal,
            cancellationToken);

    public Task<CareerQualityCorrectionConfirmation> CorrectAndRefreshAsync(
        CareerQualityCorrectionRequest request,
        CancellationToken cancellationToken)
        => coordinator.CorrectCareerQualityAsync(request, cancellationToken);
}

/// <summary>
/// Phone orchestration boundary. Every transition rereads Presentation's
/// atomic workspace and verifies the clean RunnerSession revision. Exceptions
/// never trigger mutation retries; the durable Applying lock is resolved from
/// the receipt projection instead.
/// </summary>
public sealed class Sr5CareerQualityCoordinator(
    ISr5CareerQualityPresenter presenter,
    ISr5CareerCheckpointOwnerAuthority ownerAuthority)
{
    public async Task<CareerQualityEditorState?> PrepareAsync(
        CancellationToken cancellationToken = default)
    {
        Sr5CareerRunnerBinding before = presenter.Binding;
        RequireCleanSr5(before);
        CareerQualityEditorState? editor = await presenter.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        Sr5CareerRunnerBinding after = presenter.Binding;
        RequireCleanSr5(after);
        if (editor is null)
        {
            return null;
        }
        ValidateEditor(after, editor, ownerAuthority.CurrentOwnerId);
        return editor;
    }

    public async Task<Sr5CareerQualityDraft> ReviewAsync(
        CareerQualityEditorState editor,
        CharacterCareerQualityQuote selected,
        Guid transactionId,
        DateTime expenseDateLocal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(selected);
        Sr5CareerRunnerBinding before = presenter.Binding;
        RequireCleanSr5(before);
        ValidateEditor(before, editor, ownerAuthority.CurrentOwnerId);

        CareerQualityDraft requested = CareerQualityWorkflow.CreateDraft(editor, selected);
        CareerQualityReview refreshed = await presenter.ReviewAsync(
                requested,
                cancellationToken)
            .ConfigureAwait(false);
        Sr5CareerRunnerBinding after = presenter.Binding;
        RequireCleanSr5(after);
        string blocker = string.Empty;
        if (after != before
            || !Sr5CareerQualityDraft.TryCreate(
                editor,
                refreshed,
                ownerAuthority.CurrentOwnerId,
                transactionId,
                expenseDateLocal,
                out Sr5CareerQualityDraft draft,
                out blocker))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(blocker)
                    ? "The SR5 runner or quality authority changed during review."
                    : blocker);
        }
        return draft;
    }

    public async Task<Sr5CareerQualityApplyResult> ApplyAsync(
        Sr5CareerQualityDraft draft,
        Sr5CareerQualityCheckpoint applyingCheckpoint,
        Sr5CareerQualityCheckpointStore checkpointStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(applyingCheckpoint);
        ArgumentNullException.ThrowIfNull(checkpointStore);
        using IDisposable applyingLease =
            await checkpointStore.AcquireDurableApplyingLeaseAsync(
                    applyingCheckpoint,
                    cancellationToken)
                .ConfigureAwait(false);
        Sr5CareerRunnerBinding before = presenter.Binding;
        RequireCleanSr5(before);
        if (ownerAuthority.CurrentOwnerId != draft.OwnerId
            || !draft.Matches(before.WorkspaceId, before.ContentRevision, before.SavedRevision)
            || !draft.IsExact()
            || !applyingCheckpoint.MatchesActionDraft(draft))
        {
            throw new InvalidOperationException(
                "The reviewed quality operation no longer owns the exact clean runner revision.");
        }

        try
        {
            CareerQualityConfirmation confirmation = await presenter.ConfirmAndRefreshAsync(
                    draft.Review,
                    draft.TransactionId,
                    draft.ExpenseDateLocal,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!ReceiptMatchesDraft(draft, confirmation.Receipt))
            {
                return UnknownResult(
                    draft,
                    applyingCheckpoint,
                    "The atomic quality response did not match the reviewed receipt.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The commit boundary may have succeeded before transport failed.
            // Never replay here; resolve once from fresh durable authority.
        }

        Sr5CareerQualityRecoveryResolution resolution = await ResolveAsync(
                applyingCheckpoint,
                cancellationToken)
            .ConfigureAwait(false);
        return resolution.Status switch
        {
            Sr5CareerQualityRecoveryStatus.AppliedVerified when resolution.Receipt is { } receipt =>
                new Sr5CareerQualityApplyResult(
                    Sr5CareerQualityApplyStatus.Applied,
                    draft.ActionPlan,
                    checked(draft.ExpectedWorkspaceRevision + 1),
                    receipt,
                    resolution,
                    resolution.Message),
            Sr5CareerQualityRecoveryStatus.NotAppliedVerified =>
                new Sr5CareerQualityApplyResult(
                    Sr5CareerQualityApplyStatus.RejectedBeforeMutation,
                    draft.ActionPlan,
                    null,
                    null,
                    resolution,
                    resolution.Message),
            _ => UnknownResult(draft, applyingCheckpoint, resolution.Message, resolution)
        };
    }

    public async Task<Sr5CareerQualityRecoveryResolution> ResolveAsync(
        Sr5CareerQualityCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Sr5CareerRunnerBinding before = presenter.Binding;
        RequireCleanSr5(before);
        if (!checkpoint.IsStructurallyValid()
            || ownerAuthority.CurrentOwnerId == Guid.Empty
            || ownerAuthority.CurrentOwnerId != checkpoint.Draft.OwnerId
            || before.WorkspaceId != checkpoint.Draft.WorkspaceId)
        {
            throw new InvalidOperationException(
                "The quality recovery lock does not belong to this authenticated SR5 owner and runner.");
        }

        CareerQualityEditorState? editor = await presenter.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        Sr5CareerRunnerBinding after = presenter.Binding;
        RequireCleanSr5(after);
        if (before != after)
        {
            return Unknown(checkpoint, "The runner changed during authoritative quality outcome lookup.");
        }
        return Resolve(checkpoint, after, editor);
    }

    public async Task<CharacterCareerQualityCorrectionPlan> CorrectAsync(
        Sr5CareerQualityCheckpoint checkpoint,
        CharacterCareerQualityReceipt receipt,
        Guid correctionId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(receipt);
        Sr5CareerRunnerBinding before = presenter.Binding;
        RequireCleanSr5(before);
        long expectedRevision = checked(checkpoint.Draft.ExpectedWorkspaceRevision + 1);
        if (!checkpoint.IsStructurallyValid()
            || checkpoint.Phase != Sr5CareerCheckpointPhase.Applied
            || !ReceiptMatchesDraft(checkpoint.Draft, receipt)
            || ownerAuthority.CurrentOwnerId != checkpoint.Draft.OwnerId
            || before.WorkspaceId != checkpoint.Draft.WorkspaceId
            || before.ContentRevision != expectedRevision
            || before.SavedRevision != expectedRevision
            || correctionId == Guid.Empty
            || correctionId == receipt.TransactionId
            || string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException(
                "The compensating quality correction does not own the exact saved receipt revision.");
        }

        CareerQualityCorrectionRequest request = new(
            WorkspaceId: checkpoint.Draft.WorkspaceId,
            ExpectedOwnerId: receipt.OwnerId,
            ExpectedWorkspaceRevision: expectedRevision,
            ExpectedSavedRevision: expectedRevision,
            ExpectedRulesetId: CharacterCareerQualityRules.RulesetId,
            OriginalReceipt: receipt,
            ExpectedReceiptDigest: receipt.ReceiptDigest,
            Confirmed: true,
            CorrectionId: correctionId,
            Reason: reason.Trim());
        CareerQualityCorrectionConfirmation confirmation =
            await presenter.CorrectAndRefreshAsync(request, cancellationToken)
                .ConfigureAwait(false);
        CharacterCareerQualityCorrectionPlan correction = confirmation.Correction;
        Sr5CareerRunnerBinding after = presenter.Binding;
        if (!CharacterCareerQualityRules.IsCoherent(correction)
            || correction.CorrectionId != correctionId
            || correction.OriginalTransactionId != receipt.TransactionId
            || correction.Identity != receipt.Identity
            || !correction.RestoreInstances.SequenceEqual(receipt.InstancesBefore)
            || correction.SavedCharacterKarma != receipt.CharacterKarmaBefore
            || correction.RemoveExpense != receipt.CreatesExpense
            || correction.ExpenseIdToRemove != receipt.ExpenseId
            || !string.Equals(correction.ExpectedRuntimeFingerprint, receipt.RuntimeFingerprint, StringComparison.Ordinal)
            || !string.Equals(correction.ExpectedContentDigest, receipt.ContentDigest, StringComparison.Ordinal)
            || !string.Equals(correction.OriginalReceiptDigest, receipt.ReceiptDigest, StringComparison.Ordinal)
            || confirmation.PersistedState.WorkspaceRevision != expectedRevision + 1
            || confirmation.PersistedState.SavedRevision != expectedRevision + 1
            || after.WorkspaceId != checkpoint.Draft.WorkspaceId
            || after.ContentRevision != expectedRevision + 1
            || after.SavedRevision != expectedRevision + 1
            || after.IsDirty
            || !string.IsNullOrWhiteSpace(after.Error))
        {
            throw new InvalidOperationException(
                "The compensating quality correction was not recovered from one exact atomic saved revision.");
        }
        return correction;
    }

    internal static Sr5CareerQualityRecoveryResolution Resolve(
        Sr5CareerQualityCheckpoint checkpoint,
        Sr5CareerRunnerBinding binding,
        CareerQualityEditorState? editor)
    {
        if (!checkpoint.IsStructurallyValid()
            || binding.WorkspaceId != checkpoint.Draft.WorkspaceId
            || editor is null
            || editor.WorkspaceId != checkpoint.Draft.WorkspaceId
            || editor.WorkspaceRevision != binding.ContentRevision
            || editor.SavedRevision != binding.SavedRevision
            || editor.OmittedCandidateCount != 0
            || editor.OmittedReceiptCount != 0
            || binding.IsDirty
            || !string.IsNullOrWhiteSpace(binding.Error))
        {
            return Unknown(checkpoint, "A fresh complete quality/receipt projection was unavailable for one saved revision.");
        }

        CharacterCareerQualityReceipt[] receipts = editor.RecoverableReceipts
            .Where(candidate => candidate.TransactionId == checkpoint.Draft.TransactionId)
            .Take(2)
            .ToArray();
        bool appliedRevision = checkpoint.Draft.ExpectedWorkspaceRevision < long.MaxValue
            && binding.ContentRevision == checkpoint.Draft.ExpectedWorkspaceRevision + 1
            && binding.SavedRevision == binding.ContentRevision;
        if (appliedRevision
            && receipts.Length == 1
            && ReceiptMatchesDraft(checkpoint.Draft, receipts[0]))
        {
            return Sr5CareerQualityRecoveryProof.Create(
                checkpoint,
                Sr5CareerQualityRecoveryStatus.AppliedVerified,
                receipts[0],
                "Fresh atomic quality and receipt projections verify the exact saved transaction.");
        }

        CharacterCareerQualityQuote[] quotes = editor.Quotes
            .Where(candidate => candidate.Operation == checkpoint.Draft.Review.Quote.Operation
                && candidate.Identity == checkpoint.Draft.Review.Quote.Identity
                && string.Equals(candidate.LogicalRevision, checkpoint.Draft.Review.Quote.LogicalRevision, StringComparison.Ordinal)
                && string.Equals(candidate.SourceRevision, checkpoint.Draft.Review.Quote.SourceRevision, StringComparison.Ordinal)
                && string.Equals(candidate.RuleDigest, checkpoint.Draft.Review.Quote.RuleDigest, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        bool notAppliedRevision = binding.ContentRevision == checkpoint.Draft.ExpectedWorkspaceRevision
            && binding.SavedRevision == checkpoint.Draft.ExpectedSavedRevision;
        if (notAppliedRevision
            && receipts.Length == 0
            && quotes.Length == 1
            && CharacterCareerQualityRules.IsCoherent(quotes[0]))
        {
            return Sr5CareerQualityRecoveryProof.Create(
                checkpoint,
                Sr5CareerQualityRecoveryStatus.NotAppliedVerified,
                null,
                "Fresh typed projections prove neither the quality delta nor its receipt was atomically saved.");
        }
        return Unknown(
            checkpoint,
            "The quality state or receipt ledger conflicts with the reviewed action. Do not replay or clear it.");
    }

    internal static bool ReceiptMatchesDraft(
        Sr5CareerQualityDraft draft,
        CharacterCareerQualityReceipt receipt)
    {
        CharacterCareerQualityQuote quote = draft.Review.Quote;
        return draft.IsExact()
            && CharacterCareerQualityRules.IsCoherent(receipt)
            && receipt.TransactionId == draft.TransactionId
            && receipt.Operation == quote.Operation
            && receipt.Identity == quote.Identity
            && receipt.Definition == quote.Definition
            && string.Equals(receipt.Extra, quote.Extra, StringComparison.Ordinal)
            && string.Equals(receipt.SourceName, quote.SourceName, StringComparison.Ordinal)
            && receipt.InstancesBefore.SequenceEqual(quote.InstancesBefore)
            && receipt.AffectedInternalIds.SequenceEqual(quote.AffectedInternalIds)
            && receipt.CharacterKarmaBefore == quote.AvailableKarma
            && receipt.CharacterKarmaAfter == quote.AvailableKarma + quote.CharacterKarmaDelta
            && receipt.CreatesExpense == quote.CreatesExpense
            && receipt.ExpenseId == (quote.CreatesExpense ? draft.TransactionId : Guid.Empty)
            && receipt.ExpenseDateLocal == draft.ExpenseDateLocal
            && receipt.ExpenseAmount == quote.CharacterKarmaDelta
            && string.Equals(receipt.ExpenseReason, quote.ExpenseReason, StringComparison.Ordinal)
            && receipt.ExpenseRefund == quote.ExpenseRefund
            && string.Equals(receipt.OwnerId, quote.Binding.OwnerId, StringComparison.Ordinal)
            && string.Equals(receipt.WorkspaceId, quote.Binding.WorkspaceId, StringComparison.Ordinal)
            && receipt.WorkspaceRevisionBefore == draft.ExpectedWorkspaceRevision
            && receipt.WorkspaceRevisionAfter == draft.ExpectedWorkspaceRevision + 1
            && receipt.SavedRevisionBefore == draft.ExpectedSavedRevision
            && receipt.SavedRevisionAfter == draft.ExpectedSavedRevision + 1
            && string.Equals(receipt.RuntimeFingerprint, draft.RuntimeAuthority.RuntimeDigest, StringComparison.Ordinal)
            && string.Equals(receipt.ContentDigest, draft.RuntimeAuthority.ContentDigest, StringComparison.Ordinal)
            && string.Equals(receipt.SourceRevisionBefore, quote.SourceRevision, StringComparison.Ordinal)
            && string.Equals(receipt.RuleDigestBefore, quote.RuleDigest, StringComparison.Ordinal)
            && string.Equals(receipt.LogicalRevisionBefore, quote.LogicalRevision, StringComparison.Ordinal);
    }

    private static void RequireCleanSr5(Sr5CareerRunnerBinding binding)
    {
        Sr5CareerRunnerGuard.RequireCreated(binding);
        if (binding.WorkspaceId is null
            || binding.ContentRevision <= 0
            || binding.SavedRevision != binding.ContentRevision
            || binding.IsDirty
            || !string.IsNullOrWhiteSpace(binding.Error))
        {
            throw new InvalidOperationException(
                "Quality transactions require one exact clean saved SR5 runner revision.");
        }
    }

    private static void ValidateEditor(
        Sr5CareerRunnerBinding binding,
        CareerQualityEditorState editor,
        Guid expectedOwner)
    {
        if (expectedOwner == Guid.Empty
            || !Guid.TryParseExact(editor.OwnerId, "D", out Guid projectedOwner)
            || projectedOwner != expectedOwner
            || binding.WorkspaceId != editor.WorkspaceId
            || binding.ContentRevision != editor.WorkspaceRevision
            || binding.SavedRevision != editor.SavedRevision
            || !string.Equals(editor.RulesetId, CharacterCareerQualityRules.RulesetId, StringComparison.Ordinal)
            || !string.Equals(editor.RuntimeFingerprint, Sr5CareerQualityRuntimeAuthority.CurrentRuntimeDigest, StringComparison.Ordinal)
            || !string.Equals(editor.ContentDigest, Sr5CareerQualityRuntimeAuthority.CurrentContentDigest, StringComparison.Ordinal)
            || editor.OmittedCandidateCount != 0
            || editor.OmittedReceiptCount != 0)
        {
            throw new InvalidOperationException(
                "The quality authority is absent, ambiguous, stale, foreign, or bound to another runtime/content generation.");
        }
    }

    private static Sr5CareerQualityRecoveryResolution Unknown(
        Sr5CareerQualityCheckpoint checkpoint,
        string message)
        => Sr5CareerQualityRecoveryProof.Create(
            checkpoint,
            Sr5CareerQualityRecoveryStatus.OutcomeUnknown,
            null,
            message);

    private static Sr5CareerQualityApplyResult UnknownResult(
        Sr5CareerQualityDraft draft,
        Sr5CareerQualityCheckpoint checkpoint,
        string message,
        Sr5CareerQualityRecoveryResolution? resolution = null)
    {
        resolution ??= Unknown(checkpoint, message);
        return new(
            Sr5CareerQualityApplyStatus.OutcomeUnknown,
            draft.ActionPlan,
            null,
            null,
            resolution,
            message);
    }
}

internal static class Sr5CareerQualityRecoveryProof
{
    private static readonly byte[] ProcessKey = RandomNumberGenerator.GetBytes(32);

    public static Sr5CareerQualityRecoveryResolution Create(
        Sr5CareerQualityCheckpoint checkpoint,
        Sr5CareerQualityRecoveryStatus status,
        CharacterCareerQualityReceipt? receipt,
        string message)
        => new(
            status,
            checkpoint.Draft.WorkspaceId.Value,
            checkpoint.Draft.OwnerId,
            checkpoint.Draft.TransactionId,
            checkpoint.Version,
            receipt,
            message,
            Sign(checkpoint, status, receipt, message));

    public static bool Verifies(
        Sr5CareerQualityCheckpoint checkpoint,
        Sr5CareerQualityRecoveryResolution resolution)
    {
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
        Sr5CareerQualityCheckpoint checkpoint,
        Sr5CareerQualityRecoveryStatus status,
        CharacterCareerQualityReceipt? receipt,
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
