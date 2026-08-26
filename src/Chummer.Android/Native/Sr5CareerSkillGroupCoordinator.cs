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

    Task<CharacterCareerSkillGroupAdvanceResult?> AdvanceAsync(
        CharacterCareerSkillGroupAdvanceCommand command,
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

    public Task<CharacterCareerSkillGroupAdvanceResult?> AdvanceAsync(
        CharacterCareerSkillGroupAdvanceCommand command,
        CancellationToken cancellationToken)
        => coordinator.AdvanceCareerSkillGroupAsync(command, cancellationToken);
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
        Sr5CareerRunnerGuard.RequireCreated(presenter.Binding);
        CareerSkillGroupAdvanceEditorState? editor =
            await presenter.LoadSkillGroupsAsync(cancellationToken).ConfigureAwait(false);
        Sr5CareerRunnerBinding after = presenter.Binding;
        Sr5CareerRunnerGuard.RequireCreated(after);
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
        Sr5CareerRunnerGuard.RequireCreated(before);
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

        Sr5CareerSkillGroupRecoveryResolution resolution;
        try
        {
            CharacterCareerSkillGroupAdvanceResult? result = await presenter
                .AdvanceAsync(draft.ToCommand(), cancellationToken)
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
                "The atomic Core skill-group result was unavailable. Keep the Applying lock and resolve it from the same command.");
        }
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
        Sr5CareerSkillGroupCheckpointStore checkpointStore,
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
                "The skill-group recovery checkpoint does not belong to the authenticated local SR5 owner and runner.");
        }

        if (checkpoint.Phase == Sr5CareerCheckpointPhase.Applied
            && checkpoint.Receipt is { } persistedReceipt
            && before.ContentRevision == checkpoint.Draft.ExpectedContentRevision + 1
            && before.SavedRevision == before.ContentRevision
            && !before.IsDirty
            && string.IsNullOrWhiteSpace(before.Error)
            && ReceiptMatchesDraft(checkpoint.Draft, persistedReceipt))
        {
            return Sr5CareerSkillGroupRecoveryProof.Create(
                checkpoint,
                Sr5CareerSkillGroupRecoveryStatus.AppliedVerified,
                persistedReceipt,
                "The durable Applied checkpoint contains the exact validated Core receipt.");
        }

        if (checkpoint.Phase != Sr5CareerCheckpointPhase.Applying)
        {
            throw new InvalidOperationException(
                "Only an exact Applying skill-group checkpoint can retry its atomic Core command.");
        }

        using IDisposable applyingLease =
            await checkpointStore.AcquireDurableApplyingLeaseAsync(
                checkpoint,
                cancellationToken).ConfigureAwait(false);
        try
        {
            CharacterCareerSkillGroupAdvanceResult? result = await presenter
                .AdvanceAsync(checkpoint.Draft.ToCommand(), cancellationToken)
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
                "The atomic Core command could not be resolved. Keep the Applying lock; no compatibility mutation was attempted.");
        }
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
        Sr5CareerRunnerGuard.RequireCreated(before);
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

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "Atomic skill-group correction authority is not exposed by the current Core service. The saved receipt remains locked for a future authoritative correction flow.");
    }

    internal static Sr5CareerSkillGroupRecoveryResolution ResolveServiceResult(
        Sr5CareerSkillGroupCheckpoint checkpoint,
        Sr5CareerRunnerBinding binding,
        CharacterCareerSkillGroupAdvanceResult? result)
    {
        if (!checkpoint.IsStructurallyValid()
            || binding.WorkspaceId != checkpoint.Draft.WorkspaceId
            || binding.IsDirty
            || !string.IsNullOrWhiteSpace(binding.Error))
        {
            return Unknown(
                checkpoint,
                "A fresh clean runner binding was unavailable for the atomic Core result.");
        }

        if (!CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeCommandDigest(
                checkpoint.Draft.ToCommand(),
                out string expectedCommandDigest)
            || result is null
            || !string.Equals(
                result.ContractName,
                CharacterCareerSkillGroupAdvanceServiceSchemas.ResultV1,
                StringComparison.Ordinal)
            || result.WorkspaceId != checkpoint.Draft.WorkspaceId
            || result.ExpectedWorkspaceRevision != checkpoint.Draft.ExpectedContentRevision
            || result.Identity != checkpoint.Draft.Quote.Identity
            || result.TransactionId != checkpoint.Draft.Plan.TransactionId
            || !string.Equals(result.CommandDigest, expectedCommandDigest, StringComparison.Ordinal))
        {
            return Unknown(
                checkpoint,
                "The atomic Core result is absent or belongs to another exact command.");
        }

        bool appliedRevision = checkpoint.Draft.ExpectedContentRevision < long.MaxValue
            && binding.ContentRevision == checkpoint.Draft.ExpectedContentRevision + 1
            && binding.SavedRevision == binding.ContentRevision;
        if (appliedRevision
            && result.Outcome is CharacterCareerSkillGroupAdvanceServiceOutcome.Applied
                or CharacterCareerSkillGroupAdvanceServiceOutcome.Replayed
            && result.CurrentWorkspaceRevision == binding.ContentRevision
            && result.ReviewedQuote is { } reviewedQuote
            && CharacterCareerSkillGroupAdvanceRules.IsCoherent(reviewedQuote)
            && reviewedQuote.Identity == checkpoint.Draft.Quote.Identity
            && string.Equals(
                reviewedQuote.LogicalRevision,
                checkpoint.Draft.Quote.LogicalRevision,
                StringComparison.Ordinal)
            && string.Equals(
                reviewedQuote.SourceRevision,
                checkpoint.Draft.Quote.SourceRevision,
                StringComparison.Ordinal)
            && string.Equals(
                reviewedQuote.RuleDigest,
                checkpoint.Draft.Quote.RuleDigest,
                StringComparison.Ordinal)
            && CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeBindingDigest(
                checkpoint.Draft.WorkspaceId,
                checkpoint.Draft.ExpectedContentRevision,
                reviewedQuote,
                out string resultBindingDigest)
            && string.Equals(
                resultBindingDigest,
                checkpoint.Draft.Binding.BindingDigest,
                StringComparison.Ordinal)
            && result.Receipt is { } receipt
            && ReceiptMatchesDraft(checkpoint.Draft, receipt)
            && CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeResultDigest(
                result with { ResultDigest = string.Empty },
                out string resultDigest)
            && string.Equals(resultDigest, result.ResultDigest, StringComparison.Ordinal))
        {
            return Sr5CareerSkillGroupRecoveryProof.Create(
                checkpoint,
                Sr5CareerSkillGroupRecoveryStatus.AppliedVerified,
                receipt,
                "The atomic Core result and receipt verify the exact saved advancement.");
        }

        bool notAppliedRevision = binding.ContentRevision == checkpoint.Draft.ExpectedContentRevision
            && binding.SavedRevision == checkpoint.Draft.ExpectedContentRevision;
        if (notAppliedRevision
            && result.Outcome is CharacterCareerSkillGroupAdvanceServiceOutcome.Invalid
                or CharacterCareerSkillGroupAdvanceServiceOutcome.Blocked
                or CharacterCareerSkillGroupAdvanceServiceOutcome.Conflict
                or CharacterCareerSkillGroupAdvanceServiceOutcome.IdempotencyConflict
                or CharacterCareerSkillGroupAdvanceServiceOutcome.Missing
            && (result.CurrentWorkspaceRevision == 0
                || result.CurrentWorkspaceRevision == checkpoint.Draft.ExpectedContentRevision)
            && result.Receipt is null)
        {
            return Sr5CareerSkillGroupRecoveryProof.Create(
                checkpoint,
                Sr5CareerSkillGroupRecoveryStatus.NotAppliedVerified,
                receipt: null,
                "Core rejected the exact command and the runner remains at the reviewed revision.");
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
