using System.Runtime.CompilerServices;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    private sealed record QualitiesReviewLoad(CharacterOverviewState Display)
    {
        public bool ConfirmationStarted { get; set; }
    }
    private readonly ConditionalWeakTable<CharacterCreationQualitiesReReviewState, QualitiesReviewLoad> _qualitiesReviewLoads = new();

    internal bool IsCreationQualitiesReReviewCurrent(CharacterCreationQualitiesReReviewState state)
        => _qualitiesReviewLoads.TryGetValue(state, out var issued)
           && IsCreationCatalogDisplayCurrent(issued.Display);

    internal Task<CharacterCreationQualitiesReReviewState?> LoadCreationQualitiesReReviewAsync(
        CharacterOverviewState display, CancellationToken token = default)
        => WithWorkspaceActivationGateAsync(async () =>
        {
            if (!IsCreationCatalogDisplayCurrent(display) || display.WorkspaceId is not { } id
                || display.DisplayOwnerContext is not { } owner
                || _ownerBoundQualitiesService is not IOwnerBoundCharacterCreationQualitiesReReviewService service)
                return null;
            var state = await Task.Run(() => ReadCreationAuthority(display, () =>
            {
                var loaded = service.LoadReReview(owner, new(id));
                return loaded is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } candidate }
                    && CreationQualitiesPhoneAuthority.IsReReviewReady(candidate, display) ? candidate : null;
            }, token), token);
            if (state is null || !IsCreationCatalogDisplayCurrent(display)) return null;
            _qualitiesReviewLoads.GetValue(state, _ => new(display));
            return state;
        }, token);

    internal Task<CreationQualitiesPhoneConfirmResult> ConfirmCreationQualitiesReReviewAsync(
        CharacterCreationQualitiesReReviewState state, bool explicitlyReviewed,
        CancellationToken token = default, Func<bool>? isCurrentPage = null)
        => WithWorkspaceActivationGateAsync(async () =>
        {
            if (!explicitlyReviewed || !_qualitiesReviewLoads.TryGetValue(state, out var issued)
                || issued.ConfirmationStarted || !IsCreationQualitiesReReviewCurrent(state)
                || isCurrentPage?.Invoke() == false)
                return RejectedQualitiesReview();
            // Full digest checks stay off the UI thread. Core rechecks the live
            // historical receipt, current budget and owner inside its atomic save.
            var checkpoint = await Task.Run(() =>
            {
                if (!CreationQualitiesPhoneAuthority.IsReReviewReady(state, issued.Display)) return null;
                var applying = CharacterCreationQualitiesCheckpoint.CreateReviewed(state.Current.Preview,
                    state.Current.PendingDraft!.SelectedOptionIds, Guid.NewGuid()) with
                { Phase = CharacterCreationQualitiesCheckpointPhase.Applying };
                return applying with { CheckpointDigest = CharacterCreationQualitiesCheckpoint.ComputeDigest(applying) };
            }, token);
            if (checkpoint is null || !IsCreationQualitiesReReviewCurrent(state) || isCurrentPage?.Invoke() == false)
                return RejectedQualitiesReview();
            token.ThrowIfCancellationRequested();
            issued.ConfirmationStarted = true;
            // No automatic retry, even on cancellation or an uncertain return.
            // A cold load observes the atomic Core draft/receipt; the stable
            // command key prevents a second mutation if the old request is retried.
            try
            {
                return await ConfirmCreationQualitiesCoreAsync(checkpoint, issued.Display, token,
                    state.HistoricalReceiptDigest);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            { return UnknownQualitiesOutcome(); }
        }, token);

    private static CreationQualitiesPhoneConfirmResult RejectedQualitiesReview()
        => new(CreationQualitiesPhoneOutcomes.RejectedBeforeMutation, null, null,
            [CharacterCreationQualitiesBlockers.RevisionConflict], MutationOutcomeKnown: true);
}
