using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public static class Sr5AfterRunProposalCatalogContract
{
    public const int MaximumProposalCount = 1_000;
}

public sealed record Sr5AfterRunProposalCatalogEntry(
    CharacterAfterRunSettlementIdentity Identity,
    Sr5AfterRunRewardContext RewardContext);

public sealed record Sr5AfterRunProposalCatalogResult(
    Sr5AfterRunCatalogStatus Status,
    IReadOnlyList<Sr5AfterRunProposalCatalogEntry> Entries,
    int OmittedProposalCount,
    IReadOnlyList<string> Blockers);

/// <summary>
/// Run/proposal discovery seam. There is deliberately no default XML or local
/// sample implementation: a governed Run Services adapter must be composed.
/// </summary>
public interface IAndroidAfterRunProposalCatalog
{
    Sr5AfterRunProposalCatalogResult Load(CharacterWorkspaceId workspaceId);
}

public interface ISr5AfterRunSettlementPresenter
{
    Sr5CareerRunnerBinding Binding { get; }

    Task<Sr5AfterRunSettlementEditorState> LoadAsync(
        CancellationToken cancellationToken);

    Task<CharacterAfterRunSettlementResult?> SettleAsync(
        CharacterAfterRunSettlementCommand command,
        CancellationToken cancellationToken);
}

internal sealed class RunnerSessionSr5AfterRunSettlementPresenter(
    RunnerSessionCoordinator coordinator) : ISr5AfterRunSettlementPresenter
{
    public Sr5CareerRunnerBinding Binding => new(
        coordinator.State.Profile?.Created == true,
        coordinator.State.Rules?.GameEdition,
        coordinator.State.WorkspaceId,
        coordinator.State.ContentRevision,
        coordinator.State.SavedRevision,
        coordinator.State.IsDirty,
        coordinator.State.Error);

    public Task<Sr5AfterRunSettlementEditorState> LoadAsync(
        CancellationToken cancellationToken)
        => coordinator.PrepareAfterRunSettlementAsync(cancellationToken);

    public Task<CharacterAfterRunSettlementResult?> SettleAsync(
        CharacterAfterRunSettlementCommand command,
        CancellationToken cancellationToken)
        => coordinator.SettleAfterRunAsync(command, cancellationToken);
}

/// <summary>
/// Native phone authority boundary for one governed SR5 After Run settlement.
/// It accepts only the exact Core quote/plan and treats every incomplete or
/// ambiguous result as OutcomeUnknown while retaining the durable apply lock.
/// </summary>
public sealed class Sr5AfterRunSettlementCoordinator(
    ISr5AfterRunSettlementPresenter presenter,
    ISr5CareerCheckpointOwnerAuthority ownerAuthority)
{
    public async Task<Sr5AfterRunSettlementEditorState> PrepareAsync(
        CancellationToken cancellationToken = default)
    {
        Sr5CareerRunnerGuard.RequireCreated(presenter.Binding);
        Sr5AfterRunSettlementEditorState editor =
            await presenter.LoadAsync(cancellationToken).ConfigureAwait(false);
        Sr5CareerRunnerBinding after = presenter.Binding;
        Sr5CareerRunnerGuard.RequireCreated(after);
        if (!editor.IsExact()
            || after.WorkspaceId != editor.WorkspaceId
            || after.ContentRevision != editor.WorkspaceRevision
            || after.SavedRevision != editor.WorkspaceRevision
            || after.IsDirty
            || !string.IsNullOrWhiteSpace(after.Error))
        {
            throw new InvalidOperationException(
                "The After Run proposal catalog is incoherent or belongs to another saved runner revision.");
        }
        return editor;
    }

    public async Task<Sr5AfterRunSettlementApplyResult> ApplyAsync(
        Sr5AfterRunSettlementDraft draft,
        Sr5AfterRunSettlementCheckpoint applyingCheckpoint,
        Sr5AfterRunSettlementCheckpointStore checkpointStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(applyingCheckpoint);
        ArgumentNullException.ThrowIfNull(checkpointStore);
        using IDisposable lease = await checkpointStore.AcquireDurableApplyingLeaseAsync(
            applyingCheckpoint,
            cancellationToken).ConfigureAwait(false);
        Sr5CareerRunnerBinding before = presenter.Binding;
        Sr5CareerRunnerGuard.RequireCreated(before);
        if (ownerAuthority.CurrentOwnerId != draft.OwnerId
            || before.WorkspaceId != draft.WorkspaceId
            || before.ContentRevision != draft.ExpectedWorkspaceRevision
            || before.SavedRevision != draft.ExpectedWorkspaceRevision
            || before.IsDirty
            || !string.IsNullOrWhiteSpace(before.Error)
            || !draft.IsExact()
            || !applyingCheckpoint.MatchesActionDraft(draft))
        {
            throw new InvalidOperationException(
                "The exact durable After Run Applying checkpoint does not own this saved runner revision.");
        }

        Sr5AfterRunSettlementRecoveryResolution resolution;
        try
        {
            CharacterAfterRunSettlementResult? result = await presenter
                .SettleAsync(draft.ToCommand(), cancellationToken)
                .ConfigureAwait(false);
            resolution = ResolveServiceResult(
                applyingCheckpoint,
                presenter.Binding,
                result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            resolution = Unknown(
                applyingCheckpoint,
                "The atomic Core After Run result is unavailable. Keep the Applying lock and resolve only by replaying this exact command.");
        }

        return resolution.Status switch
        {
            Sr5AfterRunSettlementRecoveryStatus.AppliedVerified
                when resolution.Receipt is { } receipt =>
                new Sr5AfterRunSettlementApplyResult(
                    Sr5AfterRunSettlementApplyStatus.Applied,
                    draft.ActionPlan,
                    checked(draft.ExpectedWorkspaceRevision + 1),
                    receipt,
                    resolution,
                    resolution.Message),
            Sr5AfterRunSettlementRecoveryStatus.NotAppliedVerified =>
                new Sr5AfterRunSettlementApplyResult(
                    Sr5AfterRunSettlementApplyStatus.RejectedBeforeMutation,
                    draft.ActionPlan,
                    null,
                    null,
                    resolution,
                    resolution.Message),
            _ => new Sr5AfterRunSettlementApplyResult(
                Sr5AfterRunSettlementApplyStatus.OutcomeUnknown,
                draft.ActionPlan,
                null,
                null,
                resolution,
                resolution.Message)
        };
    }

    public async Task<Sr5AfterRunSettlementRecoveryResolution> ResolveAsync(
        Sr5AfterRunSettlementCheckpoint checkpoint,
        Sr5AfterRunSettlementCheckpointStore checkpointStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(checkpointStore);
        Sr5CareerRunnerBinding before = presenter.Binding;
        Sr5CareerRunnerGuard.RequireCreated(before);
        if (!checkpoint.IsStructurallyValid()
            || ownerAuthority.CurrentOwnerId == Guid.Empty
            || ownerAuthority.CurrentOwnerId != checkpoint.Draft.OwnerId
            || before.WorkspaceId != checkpoint.Draft.WorkspaceId)
        {
            throw new InvalidOperationException(
                "The After Run recovery checkpoint does not belong to this owner and runner.");
        }

        if (checkpoint.Phase == Sr5CareerCheckpointPhase.Applied
            && checkpoint.Receipt is { } persistedReceipt
            && before.ContentRevision == checkpoint.Draft.ExpectedWorkspaceRevision + 1
            && before.SavedRevision == before.ContentRevision
            && !before.IsDirty
            && string.IsNullOrWhiteSpace(before.Error)
            && ReceiptMatchesDraft(checkpoint.Draft, persistedReceipt))
        {
            return Sr5AfterRunSettlementRecoveryProof.Create(
                checkpoint,
                Sr5AfterRunSettlementRecoveryStatus.AppliedVerified,
                persistedReceipt,
                "The durable Applied checkpoint contains the exact validated Core After Run receipt.");
        }

        if (checkpoint.Phase != Sr5CareerCheckpointPhase.Applying)
        {
            throw new InvalidOperationException(
                "Only an exact Applying After Run checkpoint can retry its atomic Core command.");
        }

        using IDisposable lease = await checkpointStore.AcquireDurableApplyingLeaseAsync(
            checkpoint,
            cancellationToken).ConfigureAwait(false);
        try
        {
            CharacterAfterRunSettlementResult? result = await presenter
                .SettleAsync(checkpoint.Draft.ToCommand(), cancellationToken)
                .ConfigureAwait(false);
            return ResolveServiceResult(checkpoint, presenter.Binding, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Unknown(
                checkpoint,
                "The atomic Core command could not be resolved. Keep the Applying lock; no generic mutation was attempted.");
        }
    }

    private static Sr5AfterRunSettlementRecoveryResolution ResolveServiceResult(
        Sr5AfterRunSettlementCheckpoint checkpoint,
        Sr5CareerRunnerBinding binding,
        CharacterAfterRunSettlementResult? result)
    {
        CharacterAfterRunSettlementCommand command = checkpoint.Draft.ToCommand();
        if (result is null
            || !CharacterAfterRunSettlementServiceIntegrity.TryComputeCommandDigest(
                command,
                out string commandDigest)
            || !string.Equals(
                result.ContractName,
                CharacterAfterRunSettlementServiceSchemas.ResultV1,
                StringComparison.Ordinal)
            || result.WorkspaceId != command.WorkspaceId
            || result.ExpectedWorkspaceRevision != command.ExpectedWorkspaceRevision
            || result.Identity != command.Identity
            || result.TransactionId != command.TransactionId
            || !FixedEquals(result.CommandDigest, commandDigest))
        {
            return Unknown(
                checkpoint,
                "Core did not return a result bound to this exact After Run command.");
        }

        bool appliedRevision = checkpoint.Draft.ExpectedWorkspaceRevision < long.MaxValue
            && binding.ContentRevision
                == checkpoint.Draft.ExpectedWorkspaceRevision + 1
            && binding.SavedRevision == binding.ContentRevision
            && !binding.IsDirty
            && string.IsNullOrWhiteSpace(binding.Error);
        if (appliedRevision
            && result.Outcome is CharacterAfterRunSettlementServiceOutcome.Applied
                or CharacterAfterRunSettlementServiceOutcome.Replayed
            && result.CurrentWorkspaceRevision == binding.ContentRevision
            && result.ReviewedQuote is { } reviewed
            && CharacterAfterRunSettlementRules.IsCoherent(reviewed)
            && reviewed.Identity == checkpoint.Draft.Quote.Identity
            && FixedEquals(
                reviewed.SourceDigest,
                checkpoint.Draft.Quote.SourceDigest)
            && FixedEquals(
                reviewed.CustomDataDigest,
                checkpoint.Draft.Quote.CustomDataDigest)
            && FixedEquals(
                reviewed.GmPolicyDigest,
                checkpoint.Draft.Quote.GmPolicyDigest)
            && FixedEquals(
                reviewed.RuntimeDigest,
                checkpoint.Draft.Quote.RuntimeDigest)
            && FixedEquals(
                reviewed.LogicalDigest,
                checkpoint.Draft.Quote.LogicalDigest)
            && FixedEquals(
                reviewed.GmReviewDigest,
                checkpoint.Draft.Quote.GmReviewDigest)
            && FixedEquals(
                reviewed.OwnerReviewDigest,
                checkpoint.Draft.Quote.OwnerReviewDigest)
            && CharacterAfterRunSettlementServiceIntegrity.TryComputeBindingDigest(
                checkpoint.Draft.WorkspaceId,
                checkpoint.Draft.ExpectedWorkspaceRevision,
                reviewed,
                out string bindingDigest)
            && FixedEquals(
                bindingDigest,
                checkpoint.Draft.Binding.BindingDigest)
            && result.Receipt is { } receipt
            && ReceiptMatchesDraft(checkpoint.Draft, receipt)
            && CharacterAfterRunSettlementServiceIntegrity.TryComputeResultDigest(
                result with { ResultDigest = string.Empty },
                out string resultDigest)
            && FixedEquals(resultDigest, result.ResultDigest))
        {
            return Sr5AfterRunSettlementRecoveryProof.Create(
                checkpoint,
                Sr5AfterRunSettlementRecoveryStatus.AppliedVerified,
                receipt,
                "The atomic Core After Run result, saved revision, and receipt were verified.");
        }

        bool notAppliedRevision = binding.ContentRevision
                == checkpoint.Draft.ExpectedWorkspaceRevision
            && binding.SavedRevision == checkpoint.Draft.ExpectedWorkspaceRevision
            && !binding.IsDirty
            && string.IsNullOrWhiteSpace(binding.Error);
        if (notAppliedRevision
            && result.Outcome is CharacterAfterRunSettlementServiceOutcome.Invalid
                or CharacterAfterRunSettlementServiceOutcome.Blocked
                or CharacterAfterRunSettlementServiceOutcome.Conflict
                or CharacterAfterRunSettlementServiceOutcome.IdempotencyConflict
                or CharacterAfterRunSettlementServiceOutcome.Missing
            && (result.CurrentWorkspaceRevision == 0
                || result.CurrentWorkspaceRevision
                    == checkpoint.Draft.ExpectedWorkspaceRevision)
            && result.Receipt is null)
        {
            return Sr5AfterRunSettlementRecoveryProof.Create(
                checkpoint,
                Sr5AfterRunSettlementRecoveryStatus.NotAppliedVerified,
                receipt: null,
                "Core rejected or could not serve the command and the exact saved runner revision is unchanged.");
        }

        return Unknown(
            checkpoint,
            "The authoritative After Run state is partial or conflicts with the reviewed action. Do not replay, clear, or claim success.");
    }

    internal static bool ReceiptMatchesDraft(
        Sr5AfterRunSettlementDraft draft,
        CharacterAfterRunSettlementReceipt receipt)
        => draft.IsExact()
            && CharacterAfterRunSettlementRules.IsCoherent(receipt)
            && receipt.TransactionId == draft.Plan.TransactionId
            && receipt.Identity == draft.Quote.Identity
            && receipt.HeatBefore == draft.Quote.HeatBefore
            && receipt.HeatAfter == draft.Plan.TargetHeat
            && receipt.StreetCredBefore == draft.Quote.StreetCredBefore
            && receipt.StreetCredAfter == draft.Plan.TargetStreetCred
            && receipt.NotorietyBefore == draft.Quote.NotorietyBefore
            && receipt.NotorietyAfter == draft.Plan.TargetNotoriety
            && receipt.PublicAwarenessBefore == draft.Quote.PublicAwarenessBefore
            && receipt.PublicAwarenessAfter == draft.Plan.TargetPublicAwareness
            && receipt.KarmaBefore == draft.Quote.KarmaBefore
            && receipt.KarmaAfter == draft.Plan.TargetKarma
            && receipt.ContactKarmaCost == draft.Plan.ContactKarmaCost
            && receipt.AddedContacts.SequenceEqual(draft.Plan.ContactsToAdd)
            && receipt.ExpenseId == draft.Plan.ExpenseId
            && receipt.ExpenseAmount == draft.Plan.ExpenseAmount
            && string.Equals(
                receipt.ExpenseReason,
                draft.Plan.ExpenseReason,
                StringComparison.Ordinal)
            && FixedEquals(receipt.GmReviewDigest, draft.Quote.GmReviewDigest)
            && FixedEquals(receipt.OwnerReviewDigest, draft.Quote.OwnerReviewDigest)
            && FixedEquals(receipt.SourceDigest, draft.Quote.SourceDigest)
            && FixedEquals(
                receipt.CustomDataDigest,
                draft.Quote.CustomDataDigest)
            && FixedEquals(
                receipt.GmPolicyDigest,
                draft.Quote.GmPolicyDigest)
            && FixedEquals(receipt.RuntimeDigest, draft.Quote.RuntimeDigest)
            && FixedEquals(
                receipt.LogicalDigestBefore,
                draft.Quote.LogicalDigest);

    private static Sr5AfterRunSettlementRecoveryResolution Unknown(
        Sr5AfterRunSettlementCheckpoint checkpoint,
        string message)
        => Sr5AfterRunSettlementRecoveryProof.Create(
            checkpoint,
            Sr5AfterRunSettlementRecoveryStatus.OutcomeUnknown,
            receipt: null,
            message);

    private static bool FixedEquals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
