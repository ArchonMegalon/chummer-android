using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public interface ISr5CareerKnowledgeSkillPresenter
{
    Sr5CareerRunnerBinding Binding { get; }

    Task<CareerKnowledgeSkillAdvanceEditorState?> LoadKnowledgeSkillsAsync(
        CancellationToken cancellationToken);

    Task<bool> ApplyAndSaveAsync(
        CareerKnowledgeSkillAdvanceRequest request,
        CancellationToken cancellationToken);
}
internal sealed class RunnerSessionSr5CareerKnowledgeSkillPresenter(
    RunnerSessionCoordinator coordinator) : ISr5CareerKnowledgeSkillPresenter
{
    public Sr5CareerRunnerBinding Binding => new(
        coordinator.State.Profile?.Created == true,
        coordinator.State.Rules?.GameEdition,
        coordinator.State.WorkspaceId,
        coordinator.State.ContentRevision,
        coordinator.State.SavedRevision,
        coordinator.State.IsDirty,
        coordinator.State.Error);

    public Task<CareerKnowledgeSkillAdvanceEditorState?> LoadKnowledgeSkillsAsync(
        CancellationToken cancellationToken)
        => coordinator.PrepareCareerKnowledgeSkillAdvanceAsync(cancellationToken);

    public Task<bool> ApplyAndSaveAsync(
        CareerKnowledgeSkillAdvanceRequest request,
        CancellationToken cancellationToken)
        => coordinator.ApplyCareerKnowledgeSkillAdvanceAsync(request, cancellationToken);
}

/// <summary>
/// Public SR5 knowledge skill action boundary. It accepts only a reviewed typed Core
/// plan and verifies the post-save result from a fresh Presentation projection.
/// </summary>
public sealed class Sr5CareerKnowledgeSkillCoordinator(
    ISr5CareerKnowledgeSkillPresenter presenter,
    ISr5CareerCheckpointOwnerAuthority ownerAuthority)
{
    public async Task<CareerKnowledgeSkillAdvanceEditorState?> PrepareAsync(
        CancellationToken cancellationToken = default)
    {
        // Integration adapter for Android base 1d2bd7c. Replace these calls
        // with the integration branch's shared Sr5CareerRunnerGuard.
        Sr5CareerActiveSkillCoordinator.RequireCreatedSr5(presenter.Binding);
        CareerKnowledgeSkillAdvanceEditorState? editor =
            await presenter.LoadKnowledgeSkillsAsync(cancellationToken).ConfigureAwait(false);
        Sr5CareerRunnerBinding after = presenter.Binding;
        Sr5CareerActiveSkillCoordinator.RequireCreatedSr5(after);
        if (editor is not null
            && (after.WorkspaceId != editor.WorkspaceId
                || after.ContentRevision != editor.ContentRevision))
        {
            throw new InvalidOperationException(
                "The SR5 runner changed while its knowledge skills were being loaded.");
        }
        return editor;
    }

    public async Task<Sr5CareerKnowledgeSkillApplyResult> ApplyAsync(
        Sr5CareerKnowledgeSkillDraft draft,
        Sr5CareerKnowledgeSkillCheckpoint applyingCheckpoint,
        Sr5CareerKnowledgeSkillCheckpointStore checkpointStore,
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
                "The reviewed SR5 knowledge skill action does not own the current runner revision.");
        }
        if (!applyingCheckpoint.MatchesActionDraft(draft))
        {
            throw new InvalidOperationException(
                "The exact durable Applying checkpoint does not own this knowledge skill action.");
        }

        _ = await presenter.ApplyAndSaveAsync(draft.ToRequest(), cancellationToken)
            .ConfigureAwait(false);
        Sr5CareerKnowledgeSkillRecoveryResolution resolution = await ResolveAsync(
            applyingCheckpoint,
            cancellationToken).ConfigureAwait(false);
        return resolution.Status switch
        {
            Sr5CareerKnowledgeSkillRecoveryStatus.AppliedVerified when resolution.Receipt is { } receipt =>
                new Sr5CareerKnowledgeSkillApplyResult(
                    Sr5CareerKnowledgeSkillApplyStatus.Applied,
                    draft.ActionPlan,
                    checked(draft.ExpectedContentRevision + 1),
                    receipt,
                    resolution,
                    resolution.Message),
            Sr5CareerKnowledgeSkillRecoveryStatus.NotAppliedVerified =>
                new Sr5CareerKnowledgeSkillApplyResult(
                    Sr5CareerKnowledgeSkillApplyStatus.RejectedBeforeMutation,
                    draft.ActionPlan,
                    SavedContentRevision: null,
                    Receipt: null,
                    Resolution: resolution,
                    Message: resolution.Message),
            _ => new Sr5CareerKnowledgeSkillApplyResult(
                Sr5CareerKnowledgeSkillApplyStatus.OutcomeUnknown,
                draft.ActionPlan,
                SavedContentRevision: null,
                Receipt: null,
                Resolution: resolution,
                Message: resolution.Message)
        };
    }

    public async Task<Sr5CareerKnowledgeSkillRecoveryResolution> ResolveAsync(
        Sr5CareerKnowledgeSkillCheckpoint checkpoint,
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
                "The knowledge skill recovery checkpoint does not belong to the authenticated local SR5 owner and runner.");
        }

        CareerKnowledgeSkillAdvanceEditorState? editor =
            await presenter.LoadKnowledgeSkillsAsync(cancellationToken).ConfigureAwait(false);
        Sr5CareerRunnerBinding after = presenter.Binding;
        Sr5CareerActiveSkillCoordinator.RequireCreatedSr5(after);
        if (before.WorkspaceId != after.WorkspaceId
            || before.ContentRevision != after.ContentRevision
            || before.SavedRevision != after.SavedRevision)
        {
            return Unknown(checkpoint, "The runner changed during authoritative knowledge skill outcome lookup.");
        }

        return Resolve(checkpoint, after, editor);
    }

    internal static Sr5CareerKnowledgeSkillRecoveryResolution Resolve(
        Sr5CareerKnowledgeSkillCheckpoint checkpoint,
        Sr5CareerRunnerBinding binding,
        CareerKnowledgeSkillAdvanceEditorState? editor)
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
                "A fresh clean typed knowledge skill projection was unavailable for one saved revision.");
        }

        CharacterCareerKnowledgeSkillAdvanceReceipt[] matchingReceipts = editor.RecoverableReceipts
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
            return Sr5CareerKnowledgeSkillRecoveryProof.Create(
                checkpoint,
                Sr5CareerKnowledgeSkillRecoveryStatus.AppliedVerified,
                matchingReceipts[0],
                "Fresh typed knowledge skill and persisted receipt projections verify the exact saved advancement.");
        }

        CharacterCareerKnowledgeSkillAdvanceQuote[] matchingQuotes = editor.Skills
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
            return Sr5CareerKnowledgeSkillRecoveryProof.Create(
                checkpoint,
                Sr5CareerKnowledgeSkillRecoveryStatus.NotAppliedVerified,
                receipt: null,
                "Fresh typed projections prove that neither the knowledge skill nor its receipt transaction was saved.");
        }

        return Unknown(
            checkpoint,
            "The authoritative knowledge skill state is partial or conflicts with the reviewed action. Do not replay or clear it.");
    }

    internal static bool ReceiptMatchesDraft(
        Sr5CareerKnowledgeSkillDraft draft,
        CharacterCareerKnowledgeSkillAdvanceReceipt receipt)
        => draft.IsExact()
            && CharacterCareerKnowledgeSkillAdvanceRules.IsCoherent(receipt)
            && receipt.TransactionId == draft.Plan.ExpenseId
            && receipt.ExpenseId == draft.Plan.ExpenseId
            && receipt.Identity == draft.Quote.Identity
            && string.Equals(receipt.Name, draft.Quote.Name, StringComparison.Ordinal)
            && string.Equals(receipt.SkillType, draft.Quote.SkillType, StringComparison.Ordinal)
            && receipt.SkillKarmaBefore == draft.Quote.KarmaPoints
            && receipt.SkillKarmaAfter == draft.Plan.SavedSkillKarmaPoints
            && receipt.CharacterKarmaBefore == draft.Quote.AvailableKarma
            && receipt.CharacterKarmaAfter == draft.Plan.SavedCharacterKarma
            && receipt.ExpenseAmount == draft.Plan.ExpenseAmount
            && string.Equals(receipt.CharacterRevision, draft.Quote.CharacterRevision, StringComparison.Ordinal)
            && string.Equals(receipt.LogicalRevision, draft.Quote.LogicalRevision, StringComparison.Ordinal)
            && string.Equals(receipt.SourceRevision, draft.Quote.SourceRevision, StringComparison.Ordinal)
            && string.Equals(receipt.RuleDigest, draft.Quote.RuleDigest, StringComparison.Ordinal);

    private static bool QuotesMatchExactly(
        CharacterCareerKnowledgeSkillAdvanceQuote current,
        CharacterCareerKnowledgeSkillAdvanceQuote reviewed)
    {
        // Quote record equality is shallow for its IReadOnlyList of
        // prerequisites. Compare the complete deterministic payload so a
        // fresh equivalent projection is accepted but any scalar, ordering,
        // prerequisite, or revision/digest drift stays unresolved.
        try
        {
            return CharacterCareerKnowledgeSkillAdvanceRules.IsCoherent(current)
                && CharacterCareerKnowledgeSkillAdvanceRules.IsCoherent(reviewed)
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

    private static Sr5CareerKnowledgeSkillRecoveryResolution Unknown(
        Sr5CareerKnowledgeSkillCheckpoint checkpoint,
        string message)
        => Sr5CareerKnowledgeSkillRecoveryProof.Create(
            checkpoint,
            Sr5CareerKnowledgeSkillRecoveryStatus.OutcomeUnknown,
            receipt: null,
            message);
}

internal static class Sr5CareerKnowledgeSkillRecoveryProof
{
    // A resolution is verified and consumed in one process. After process death
    // the coordinator must query the authoritative presenter again and mint a
    // new proof; no proof is persisted as checkpoint authority.
    private static readonly byte[] ProcessKey = RandomNumberGenerator.GetBytes(32);

    public static Sr5CareerKnowledgeSkillRecoveryResolution Create(
        Sr5CareerKnowledgeSkillCheckpoint checkpoint,
        Sr5CareerKnowledgeSkillRecoveryStatus status,
        CharacterCareerKnowledgeSkillAdvanceReceipt? receipt,
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
        Sr5CareerKnowledgeSkillCheckpoint checkpoint,
        Sr5CareerKnowledgeSkillRecoveryResolution resolution)
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
        Sr5CareerKnowledgeSkillCheckpoint checkpoint,
        Sr5CareerKnowledgeSkillRecoveryStatus status,
        CharacterCareerKnowledgeSkillAdvanceReceipt? receipt,
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
