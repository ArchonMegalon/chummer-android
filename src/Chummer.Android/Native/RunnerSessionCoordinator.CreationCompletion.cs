using System.Runtime.CompilerServices;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Presentation;
using Chummer.Presentation.Creation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    private readonly IOwnerBoundCharacterCreationCompletionReader? _creationCompletionReader;
    private readonly IOwnerBoundCharacterCreationCompletionService? _creationCompletionService;
    private const string CompletionOutcomeUnknown = "creation-completion-apply-outcome-unknown";

    private sealed record CompletionInputsDisplay(CharacterOverviewState Display, string SnapshotDigest);
    private sealed record CompletionProposalDisplay(CharacterOverviewState Display, string ProposalDigest);
    private sealed class CompletionReviewDisplay(
        CharacterOverviewState display, CharacterCreationCompletionApplyReview frozen, string digest)
    {
        public CharacterOverviewState Display { get; } = display;
        public CharacterCreationCompletionApplyReview Frozen { get; } = frozen;
        public string Digest { get; } = digest;
        public volatile bool ApplyStarted;
        public CharacterCreationCompletionApplyRequest? Command { get; set; }
        public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt>? Result { get; set; }
    }

    private readonly ConditionalWeakTable<CharacterCreationCompletionReviewInputs, CompletionInputsDisplay> _completionInputs = new();
    private readonly ConditionalWeakTable<CompletionAssistantProposal, CompletionProposalDisplay> _completionProposals = new();
    private readonly ConditionalWeakTable<CharacterCreationCompletionApplyReview, CompletionReviewDisplay> _completionReviews = new();

    private bool CompletionDisplayCurrent(CharacterOverviewState original)
        => original.Profile?.Created == false
           && original.DisplayOwnerContext is { IsValid: true }
           && !State.IsBusy && State.ConflictState is null
           && IsCreationFinalizationDisplayCurrent(original);

    internal bool IsCreationCompletionInputsCurrent(CharacterCreationCompletionReviewInputs inputs)
        => _completionInputs.TryGetValue(inputs, out var issued) && CompletionDisplayCurrent(issued.Display);

    internal bool IsCreationCompletionProposalCurrent(CompletionAssistantProposal proposal)
        => _completionProposals.TryGetValue(proposal, out var issued) && CompletionDisplayCurrent(issued.Display);

    internal bool IsCreationCompletionReviewCurrent(CharacterCreationCompletionApplyReview review)
        => _completionReviews.TryGetValue(review, out var issued) && !issued.ApplyStarted
           && CompletionDisplayCurrent(issued.Display);

    public Task<CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs>>
        LoadCreationCompletionInputsAsync(CancellationToken cancellationToken = default)
    {
        var original = State;
        return WithWorkspaceActivationGateAsync(() => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CompletionDisplayCurrent(original) || original.WorkspaceId is not { } workspaceId
                || original.DisplayOwnerContext is not { } owner || _creationCompletionReader is not { } reader)
                return CompletionUnavailable<CharacterCreationCompletionReviewInputs>();
            var result = ReadCreationAuthority(original,
                () => reader.LoadCompletionInputs(owner, new(workspaceId), cancellationToken), cancellationToken);
            if (!CompletionDisplayCurrent(original)) return CompletionUnavailable<CharacterCreationCompletionReviewInputs>();
            if (result.Value is { } inputs)
            {
                if (result.Outcome != CharacterCreationFoundationOutcomes.Success
                    || inputs.Skills.Binding.WorkspaceId != workspaceId
                    || inputs.Skills.Binding.ContentRevision != original.ContentRevision
                    || inputs.Skills.Binding.SavedRevision != original.SavedRevision)
                    return CompletionUnavailable<CharacterCreationCompletionReviewInputs>();
                _completionInputs.GetValue(inputs, _ => new(original, inputs.SnapshotDigest));
            }
            return result;
        }, cancellationToken), cancellationToken);
    }

    public Task<CharacterCreationFoundationResult<CompletionAssistantProposal>> BuildCreationCompletionProposalAsync(
        CharacterCreationCompletionReviewInputs inputs, CompletionAssistantIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(intent);
        return WithWorkspaceActivationGateAsync(() => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_completionInputs.TryGetValue(inputs, out var issued) || !CompletionDisplayCurrent(issued.Display)
                || intent.OwnerContext != issued.Display.DisplayOwnerContext
                || intent.InputsSnapshotDigest != issued.SnapshotDigest || inputs.SnapshotDigest != issued.SnapshotDigest)
                return CompletionUnavailable<CompletionAssistantProposal>();
            // The shared builder detaches inputs and independently verifies their
            // digest. Android neither allocates points nor changes draft stages.
            var result = CompletionRecommendationBuilder.BuildMissingOnly(inputs, intent, cancellationToken);
            if (!CompletionDisplayCurrent(issued.Display)) return CompletionUnavailable<CompletionAssistantProposal>();
            if (result.Value is { } proposal)
                _completionProposals.GetValue(proposal, _ => new(issued.Display, proposal.ProposalDigest));
            return result;
        }, cancellationToken), cancellationToken);
    }

    public Task<CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource>> LoadCreationCompletionStartingCashAsync(
        CompletionAssistantProposal proposal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return WithWorkspaceActivationGateAsync(() => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_completionProposals.TryGetValue(proposal, out var issued) || !CompletionDisplayCurrent(issued.Display)
                || issued.Display.DisplayOwnerContext is not { } owner || issued.Display.WorkspaceId is not { } workspaceId
                || _creationCompletionService is not { } service)
                return CompletionUnavailable<CharacterCreationStartingNuyenSource>();
            var frozen = FreezeCompletionValue(proposal);
            // Preserve the issued in-process trusted-local stamp, never rebuild
            // authority from the owner name in a serialized proposal.
            frozen = frozen with { Intent = frozen.Intent with { OwnerContext = proposal.Intent.OwnerContext } };
            if (frozen.ProposalDigest != issued.ProposalDigest
                || CompletionAssistantRules.ComputeProposalDigest(frozen) != issued.ProposalDigest
                || frozen.Intent.OwnerContext != owner || frozen.Evaluation.Value is not { } plan)
                return CompletionUnavailable<CharacterCreationStartingNuyenSource>();
            var request = new CharacterCreationCompletionReviewRequest(frozen.Intent.InputsSnapshotDigest, plan.Selections);
            var result = ReadCreationAuthority(issued.Display,
                () => service.LoadStartingCash(owner, workspaceId, request, cancellationToken), cancellationToken);
            if (!CompletionDisplayCurrent(issued.Display)
                || CompletionAssistantRules.ComputeProposalDigest(proposal) != issued.ProposalDigest)
                return CompletionUnavailable<CharacterCreationStartingNuyenSource>();
            if (result.Value is { } source && (result.Outcome != CharacterCreationFoundationOutcomes.Success
                || !CharacterCreationKarmaFinalizationBudgetRules.IsValidStartingCashSource(source)))
                return CompletionUnavailable<CharacterCreationStartingNuyenSource>();
            return result;
        }, cancellationToken), cancellationToken);
    }

    public Task<CharacterCreationFoundationResult<CharacterCreationCompletionApplyReview>> ReviewCreationCompletionAsync(
        CompletionAssistantProposal proposal, CharacterCreationStartingCashChoice startingCash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(startingCash);
        return WithWorkspaceActivationGateAsync(() => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_completionProposals.TryGetValue(proposal, out var issued) || !CompletionDisplayCurrent(issued.Display)
                || issued.Display.DisplayOwnerContext is not { } owner || issued.Display.WorkspaceId is not { } workspaceId
                || _creationCompletionService is not { } service)
                return CompletionUnavailable<CharacterCreationCompletionApplyReview>();
            var frozen = FreezeCompletionValue(proposal);
            // JSON cannot recreate OwnerScope's trusted-local identity. Retain
            // the exact in-process stamp, still checked against the issued
            // display below; never infer local authority from its stored name.
            frozen = frozen with { Intent = frozen.Intent with { OwnerContext = proposal.Intent.OwnerContext } };
            if (frozen.ProposalDigest != issued.ProposalDigest
                || CompletionAssistantRules.ComputeProposalDigest(frozen) != issued.ProposalDigest
                || frozen.Intent.OwnerContext != owner || frozen.Evaluation.Value is not { } plan)
                return CompletionUnavailable<CharacterCreationCompletionApplyReview>();
            var request = new CharacterCreationCompletionReviewRequest(frozen.Intent.InputsSnapshotDigest, plan.Selections);
            var result = ReadCreationAuthority(issued.Display,
                () => service.Review(owner, workspaceId, request, startingCash, cancellationToken), cancellationToken);
            if (!CompletionDisplayCurrent(issued.Display)) return CompletionUnavailable<CharacterCreationCompletionApplyReview>();
            if (result.Value is { } review)
            {
                var retained = FreezeCompletionValue(review);
                var projection = retained.Projection;
                if (result.Outcome != CharacterCreationFoundationOutcomes.Success
                    || projection.Binding.WorkspaceId != workspaceId
                    || projection.Binding.ContentRevision != issued.Display.ContentRevision
                    || projection.Binding.SavedRevision != issued.Display.SavedRevision
                    || projection.InputsSnapshotDigest != request.InputsSnapshotDigest
                    || projection.StartingCash.Choice != startingCash
                    || CharacterCreationFinalizationDigest.Compute(retained.Proposal) != CharacterCreationFinalizationDigest.Compute(request)
                    || projection.ProjectionDigest != CharacterCreationCompletionFinalizationRules.ComputeProjectionDigest(projection))
                    return CompletionUnavailable<CharacterCreationCompletionApplyReview>();
                _completionReviews.GetValue(review, _ => new(issued.Display, retained,
                    CharacterCreationFinalizationDigest.Compute(retained)));
            }
            return result;
        }, cancellationToken), cancellationToken);
    }

    public Task<CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt>> ApplyCreationCompletionAsync(
        CharacterCreationCompletionApplyReview review, string idempotencyKey, bool explicitlyConfirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (!explicitlyConfirmed)
            return Task.FromResult(CompletionUnavailable<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired));
        if (!_completionReviews.TryGetValue(review, out var issued))
            return Task.FromResult(CompletionUnavailable<CharacterCreationFinalizationReceipt>());
        return WithWorkspaceActivationGateAsync(
            () => ApplyIssuedCreationCompletionAsync(review, issued, idempotencyKey, cancellationToken), cancellationToken);
    }

    private async Task<CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt>> ApplyIssuedCreationCompletionAsync(
        CharacterCreationCompletionApplyReview review, CompletionReviewDisplay issued,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        if (issued.Display.DisplayOwnerContext is not { } owner || _creationCompletionService is not { } service)
            return CompletionUnavailable<CharacterCreationFinalizationReceipt>();
        if (issued.ApplyStarted)
        {
            if (issued.Command?.IdempotencyKey != idempotencyKey)
                return CompletionUnavailable<CharacterCreationFinalizationReceipt>(CharacterCreationFinalizationBlockers.IdempotencyConflict);
            // A dispatched review is consumed. Recovery observes the exact key;
            // it never calls Apply again, even if the first outcome was unknown.
            return await RecoverCreationCompletionAsync(issued, cancellationToken);
        }
        if (!CompletionDisplayCurrent(issued.Display)
            || await Task.Run(() => CharacterCreationFinalizationDigest.Compute(review), cancellationToken) != issued.Digest
            || !CompletionDisplayCurrent(issued.Display))
            return CompletionUnavailable<CharacterCreationFinalizationReceipt>();
        var projection = issued.Frozen.Projection;
        var command = new CharacterCreationCompletionApplyRequest(projection.Binding, issued.Frozen.Proposal,
            projection.ReviewDigest, projection.StartingCash.Choice, projection.ProjectionDigest,
            idempotencyKey, ExplicitlyConfirmed: true);
        if (!CharacterCreationCompletionTransaction.IsConfirmed(command))
            return CompletionUnavailable<CharacterCreationFinalizationReceipt>(CharacterCreationFinalizationBlockers.IdempotencyKeyInvalid);
        cancellationToken.ThrowIfCancellationRequested();
        issued.Command = command;
        issued.ApplyStarted = true;
        int entered = 0;
        try
        {
            issued.Result = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CompletionDisplayCurrent(issued.Display)) return CompletionUnavailable<CharacterCreationFinalizationReceipt>();
                Interlocked.Exchange(ref entered, 1);
                // Core acquires/releases its thread-affine owner lease inside
                // this single synchronous delegate; none crosses the await.
                return service.Apply(owner, command, cancellationToken);
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (Volatile.Read(ref entered) == 0)
        {
            issued.Command = null;
            issued.ApplyStarted = false;
            throw;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            issued.Result = new("outcome-unknown", null, [CompletionOutcomeUnknown]);
        }
        if (issued.Result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } receipt }
            && CompletionReceiptMatches(issued, receipt))
            return await ReopenCreationCompletionAsync(issued, receipt, cancellationToken);
        if (issued.Result?.Outcome != "outcome-unknown" && issued.Result?.Value is null)
            return issued.Result ?? CompletionUnavailable<CharacterCreationFinalizationReceipt>();
        issued.Result = new("outcome-unknown", null, [CompletionOutcomeUnknown]);
        return await RecoverCreationCompletionAsync(issued, cancellationToken);
    }

    private async Task<CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt>> RecoverCreationCompletionAsync(
        CompletionReviewDisplay issued, CancellationToken cancellationToken)
    {
        if (issued.Result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } known }
            && CompletionReceiptMatches(issued, known))
            return await ReopenCreationCompletionAsync(issued, known, cancellationToken);
        if (issued.Result?.Outcome != "outcome-unknown")
            return issued.Result ?? CompletionUnavailable<CharacterCreationFinalizationReceipt>();
        if (issued.Command is not { } command || issued.Display.DisplayOwnerContext is not { } owner
            || !IsNativePersistenceOwnerCurrent(owner) || _ownerBoundFinalizationService is not { } service)
            return issued.Result ?? new("outcome-unknown", null, [CompletionOutcomeUnknown]);
        try
        {
            // Caller cancellation cannot hide an already committed receipt.
            // Lookup is read-only and uses the original owner and exact key.
            var lookup = await Task.Run(() => service.LookupReceipt(owner,
                new(command.Binding.WorkspaceId, command.IdempotencyKey)));
            if (lookup.Outcome == CharacterCreationFinalizationOutcomes.Replayed && lookup.Value is { } receipt
                && CompletionReceiptMatches(issued, receipt))
            {
                issued.Result = new(CharacterCreationFoundationOutcomes.Success, receipt, []);
                return await ReopenCreationCompletionAsync(issued, receipt, cancellationToken);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException) { }
        return issued.Result ?? new("outcome-unknown", null, [CompletionOutcomeUnknown]);
    }

    private async Task<CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt>> ReopenCreationCompletionAsync(
        CompletionReviewDisplay issued, CharacterCreationFinalizationReceipt receipt, CancellationToken cancellationToken)
    {
        var success = new CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt>(
            CharacterCreationFoundationOutcomes.Success, receipt, []);
        issued.Result = success;
        _finalizationReceipts.GetValue(receipt, _ => issued.Display);
        try
        {
            if (issued.Display.DisplayOwnerContext is not { } owner || !IsNativePersistenceOwnerCurrent(owner)
                || State.WorkspaceId != receipt.WorkspaceId || State.DisplayOwnerContext != owner
                || State.Session.OwnerContext != owner || _presenter is not IOwnerBoundWorkspaceRefreshPresenter bound)
                return NeedsReopen();
            await bound.LoadAsync(owner, receipt.WorkspaceId, cancellationToken);
            if (!IsCreationFinalizationReceiptCurrent(receipt)) return NeedsReopen();
            await SyncShellAsync(cancellationToken);
            if (!IsCreationFinalizationReceiptCurrent(receipt)) return NeedsReopen();
            _notice = CreationAllocationStrings.Get("Finalization.Reopened",
                "Your saved runner has reopened in Career mode.");
            NotifyChanged();
            return IsCreationFinalizationReceiptCurrent(receipt) ? success : NeedsReopen();
        }
        catch (Exception error) when (error is not OutOfMemoryException) { return NeedsReopen(); }

        CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> NeedsReopen()
            => new(CharacterCreationFoundationOutcomes.Success, receipt,
                [CharacterCreationFinalizationBlockers.PostCommitReopenRequired]);
    }

    private static bool CompletionReceiptMatches(CompletionReviewDisplay issued, CharacterCreationFinalizationReceipt receipt)
    {
        if (issued.Command is not { } command) return false;
        var binding = command.Binding;
        var projection = issued.Frozen.Projection;
        return receipt.Schema == CharacterCreationFinalizationSchemas.ReceiptV1
            && receipt.WorkspaceId == binding.WorkspaceId && receipt.CharacterCreated && receipt.RequiresFreshCareerReopen
            && receipt.PreviousContentRevision == binding.ContentRevision && receipt.ContentRevision == binding.ContentRevision + 1
            && receipt.PreviousSavedRevision == binding.SavedRevision && receipt.SavedRevision == receipt.ContentRevision
            && receipt.PreviousRawCharacterXmlDigest == binding.RawCharacterXmlDigest
            && receipt.PreviousAuxiliaryStateDigest == binding.AuxiliaryStateDigest
            && receipt.RawCharacterXmlDigest == projection.ExpectedResultRawCharacterXmlDigest
            && receipt.AuthorityDigest == binding.AuthorityDigest && receipt.BuildMethod == binding.BuildMethod
            && receipt.PreviewDigest == command.CompletionReviewDigest && receipt.PlanDigest == command.ProjectionDigest
            && receipt.StartingCash == command.StartingCash
            && receipt.StartingCashAuthorityDigest == projection.StartingCash.AuthorityDigest
            && CharacterCreationFinalizationDigest.Compute(receipt.CarryoverPolicy)
                == CharacterCreationFinalizationDigest.Compute(projection.CarryoverPolicy)
            && CharacterCreationFinalizationDigest.IsCanonical(receipt.CompletionAuthorityDigest)
            && receipt.IdempotencyKeyDigest == CharacterCreationFinalizationDigest.ComputeIdempotencyKeyDigest(command.IdempotencyKey)
            && receipt.CommandDigest == CharacterCreationCompletionTransaction.CommandDigest(command)
            && receipt.ReceiptDigest == CharacterCreationFinalizationDigest.ComputeReceiptDigest(receipt);
    }

    private static T FreezeCompletionValue<T>(T value) where T : class
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))
           ?? throw new InvalidDataException("The completion review could not be detached.");

    private static CharacterCreationFoundationResult<T> CompletionUnavailable<T>(
        string blocker = CharacterCreationFinalizationBlockers.StaleWorkspaceRevision) where T : class
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);
}
