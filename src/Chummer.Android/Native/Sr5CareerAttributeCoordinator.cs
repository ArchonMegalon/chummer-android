using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public interface ISr5CareerAttributePresenter
{
    Sr5CareerRunnerBinding Binding { get; }

    Task<CareerAttributeAdvanceEditorState?> LoadAttributesAsync(
        CancellationToken cancellationToken);

    Task<bool> ApplyAndSaveAsync(
        CareerAttributeAdvanceRequest request,
        CancellationToken cancellationToken);
}
internal sealed class RunnerSessionSr5CareerAttributePresenter(
    RunnerSessionCoordinator coordinator) : ISr5CareerAttributePresenter
{
    public Sr5CareerRunnerBinding Binding => new(
        coordinator.State.Profile?.Created == true,
        coordinator.State.Rules?.GameEdition,
        coordinator.State.WorkspaceId,
        coordinator.State.ContentRevision,
        coordinator.State.SavedRevision,
        coordinator.State.IsDirty,
        coordinator.State.Error);

    public Task<CareerAttributeAdvanceEditorState?> LoadAttributesAsync(
        CancellationToken cancellationToken)
        => coordinator.PrepareCareerAttributeAdvanceAsync(cancellationToken);

    public Task<bool> ApplyAndSaveAsync(
        CareerAttributeAdvanceRequest request,
        CancellationToken cancellationToken)
        => coordinator.ApplyCareerAttributeAdvanceAsync(request, cancellationToken);
}

/// <summary>
/// Public SR5 attribute action boundary. It accepts only a reviewed typed Core
/// plan and verifies the post-save result from a fresh Presentation projection.
/// </summary>
public sealed class Sr5CareerAttributeCoordinator(
    ISr5CareerAttributePresenter presenter,
    ISr5CareerCheckpointOwnerAuthority ownerAuthority)
{
    public async Task<CareerAttributeAdvanceEditorState?> PrepareAsync(
        CancellationToken cancellationToken = default)
    {
        Sr5CareerActiveSkillCoordinator.RequireCreatedSr5(presenter.Binding);
        CareerAttributeAdvanceEditorState? editor =
            await presenter.LoadAttributesAsync(cancellationToken).ConfigureAwait(false);
        Sr5CareerRunnerBinding after = presenter.Binding;
        Sr5CareerActiveSkillCoordinator.RequireCreatedSr5(after);
        if (editor is not null
            && (after.WorkspaceId != editor.WorkspaceId
                || after.ContentRevision != editor.ContentRevision))
        {
            throw new InvalidOperationException(
                "The SR5 runner changed while its attributes were being loaded.");
        }
        return editor;
    }

    public async Task<Sr5CareerAttributeApplyResult> ApplyAsync(
        Sr5CareerAttributeDraft draft,
        Sr5CareerAttributeCheckpoint applyingCheckpoint,
        Sr5CareerAttributeCheckpointStore checkpointStore,
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
                "The reviewed SR5 attribute action does not own the current runner revision.");
        }
        if (!applyingCheckpoint.MatchesActionDraft(draft))
        {
            throw new InvalidOperationException(
                "The exact durable Applying checkpoint does not own this attribute action.");
        }

        _ = await presenter.ApplyAndSaveAsync(draft.ToRequest(), cancellationToken)
            .ConfigureAwait(false);
        Sr5CareerAttributeRecoveryResolution resolution = await ResolveAsync(
            applyingCheckpoint,
            cancellationToken).ConfigureAwait(false);
        return resolution.Status switch
        {
            Sr5CareerAttributeRecoveryStatus.AppliedVerified when resolution.Receipt is { } receipt =>
                new Sr5CareerAttributeApplyResult(
                    Sr5CareerAttributeApplyStatus.Applied,
                    draft.ActionPlan,
                    checked(draft.ExpectedContentRevision + 1),
                    receipt,
                    resolution,
                    resolution.Message),
            Sr5CareerAttributeRecoveryStatus.NotAppliedVerified =>
                new Sr5CareerAttributeApplyResult(
                    Sr5CareerAttributeApplyStatus.RejectedBeforeMutation,
                    draft.ActionPlan,
                    SavedContentRevision: null,
                    Receipt: null,
                    Resolution: resolution,
                    Message: resolution.Message),
            _ => new Sr5CareerAttributeApplyResult(
                Sr5CareerAttributeApplyStatus.OutcomeUnknown,
                draft.ActionPlan,
                SavedContentRevision: null,
                Receipt: null,
                Resolution: resolution,
                Message: resolution.Message)
        };
    }

    public async Task<Sr5CareerAttributeRecoveryResolution> ResolveAsync(
        Sr5CareerAttributeCheckpoint checkpoint,
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
                "The attribute recovery checkpoint does not belong to the authenticated local SR5 owner and runner.");
        }

        CareerAttributeAdvanceEditorState? editor =
            await presenter.LoadAttributesAsync(cancellationToken).ConfigureAwait(false);
        Sr5CareerRunnerBinding after = presenter.Binding;
        Sr5CareerActiveSkillCoordinator.RequireCreatedSr5(after);
        if (before.WorkspaceId != after.WorkspaceId
            || before.ContentRevision != after.ContentRevision
            || before.SavedRevision != after.SavedRevision)
        {
            return Unknown(checkpoint, "The runner changed during authoritative attribute outcome lookup.");
        }

        return Resolve(checkpoint, after, editor);
    }

    internal static Sr5CareerAttributeRecoveryResolution Resolve(
        Sr5CareerAttributeCheckpoint checkpoint,
        Sr5CareerRunnerBinding binding,
        CareerAttributeAdvanceEditorState? editor)
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
                "A fresh clean typed attribute projection was unavailable for one saved revision.");
        }

        CharacterCareerAttributeAdvanceReceipt[] matchingReceipts = editor.RecoverableReceipts
            .Where(candidate => candidate.TransactionId == checkpoint.Draft.Plan.ExpenseId)
            .Take(2)
            .ToArray();
        bool appliedRevision = checkpoint.Draft.ExpectedContentRevision < long.MaxValue
            && binding.ContentRevision == checkpoint.Draft.ExpectedContentRevision + 1
            && binding.SavedRevision == binding.ContentRevision;
        if (appliedRevision
            && matchingReceipts.Length == 1
            && ReceiptMatchesDraft(checkpoint.Draft, matchingReceipts[0]))
        {
            return Sr5CareerAttributeRecoveryProof.Create(
                checkpoint,
                Sr5CareerAttributeRecoveryStatus.AppliedVerified,
                matchingReceipts[0],
                "Fresh typed attribute and persisted receipt projections verify the exact saved advancement.");
        }

        CharacterCareerAttributeAdvanceQuote[] matchingQuotes = editor.Attributes
            .Where(candidate => candidate.Identity == checkpoint.Draft.Quote.Identity)
            .Take(2)
            .ToArray();
        bool originalQuoteStillExact = matchingQuotes.Length == 1
            && QuotesMatchExactly(matchingQuotes[0], checkpoint.Draft.Quote);
        bool notAppliedRevision = binding.ContentRevision == checkpoint.Draft.ExpectedContentRevision
            && binding.SavedRevision == checkpoint.Draft.ExpectedContentRevision;
        if (notAppliedRevision
            && matchingReceipts.Length == 0
            && originalQuoteStillExact)
        {
            return Sr5CareerAttributeRecoveryProof.Create(
                checkpoint,
                Sr5CareerAttributeRecoveryStatus.NotAppliedVerified,
                receipt: null,
                "Fresh typed projections prove that neither the attribute nor its receipt transaction was saved.");
        }

        return Unknown(
            checkpoint,
            "The authoritative attribute state is partial or conflicts with the reviewed action. Do not replay or clear it.");
    }

    internal static bool ReceiptMatchesDraft(
        Sr5CareerAttributeDraft draft,
        CharacterCareerAttributeAdvanceReceipt receipt)
        => draft.IsExact()
            && CharacterCareerAttributeAdvanceRules.IsCoherent(receipt)
            && receipt.TransactionId == draft.Plan.ExpenseId
            && receipt.ExpenseId == draft.Plan.ExpenseId
            && receipt.Identity == draft.Quote.Identity
            && receipt.RepairsBurnedEdge == draft.Quote.RepairsBurnedEdge
            && receipt.AttributeKarmaBefore == draft.Quote.KarmaPoints
            && receipt.AttributeKarmaAfter == draft.Plan.SavedAttributeKarmaPoints
            && receipt.CharacterKarmaBefore == draft.Quote.AvailableKarma
            && receipt.CharacterKarmaAfter == draft.Plan.SavedCharacterKarma
            && receipt.BurnedEdgePointsBefore == draft.Quote.BurnedEdgePoints
            && receipt.BurnedEdgePointsAfter == draft.Plan.SavedBurnedEdgePoints
            && receipt.ExpenseAmount == draft.Plan.ExpenseAmount
            && string.Equals(receipt.LogicalRevision, draft.Quote.LogicalRevision, StringComparison.Ordinal)
            && string.Equals(receipt.SourceRevision, draft.Quote.SourceRevision, StringComparison.Ordinal)
            && string.Equals(receipt.RuleDigest, draft.Quote.RuleDigest, StringComparison.Ordinal);

    private static bool QuotesMatchExactly(
        CharacterCareerAttributeAdvanceQuote current,
        CharacterCareerAttributeAdvanceQuote reviewed)
    {
        // Quote record equality is shallow for its IReadOnlyList of
        // prerequisites. Compare the complete deterministic payload so a
        // fresh equivalent projection is accepted but any scalar, ordering,
        // prerequisite, or revision/digest drift stays unresolved.
        try
        {
            return CharacterCareerAttributeAdvanceRules.IsCoherent(current)
                && CharacterCareerAttributeAdvanceRules.IsCoherent(reviewed)
                && string.Equals(
                    JsonSerializer.Serialize(current),
                    JsonSerializer.Serialize(reviewed),
                    StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException
            or NotSupportedException)
        {
            return false;
        }
    }

    private static Sr5CareerAttributeRecoveryResolution Unknown(
        Sr5CareerAttributeCheckpoint checkpoint,
        string message)
        => Sr5CareerAttributeRecoveryProof.Create(
            checkpoint,
            Sr5CareerAttributeRecoveryStatus.OutcomeUnknown,
            receipt: null,
            message);
}

internal static class Sr5CareerAttributeRecoveryProof
{
    // A resolution is verified and consumed in one process. After process death
    // the coordinator must query the authoritative presenter again and mint a
    // new proof; no proof is persisted as checkpoint authority.
    private static readonly byte[] ProcessKey = RandomNumberGenerator.GetBytes(32);

    public static Sr5CareerAttributeRecoveryResolution Create(
        Sr5CareerAttributeCheckpoint checkpoint,
        Sr5CareerAttributeRecoveryStatus status,
        CharacterCareerAttributeAdvanceReceipt? receipt,
        string message)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return new(
            status,
            checkpoint.Draft.WorkspaceId.Value,
            checkpoint.Draft.OwnerId,
            checkpoint.Draft.Plan.ExpenseId,
            checkpoint.Version,
            receipt,
            message,
            Sign(checkpoint, status, receipt, message));
    }

    public static bool Verifies(
        Sr5CareerAttributeCheckpoint checkpoint,
        Sr5CareerAttributeRecoveryResolution resolution)
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
        Sr5CareerAttributeCheckpoint checkpoint,
        Sr5CareerAttributeRecoveryStatus status,
        CharacterCareerAttributeAdvanceReceipt? receipt,
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
