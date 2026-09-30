using System.Runtime.CompilerServices;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Presentation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    private sealed record MagicReviewLoad(CharacterOverviewState Display, CharacterCreationMagicResonanceReReviewState State)
    {
        public bool ConfirmationStarted { get; set; }
    }
    private readonly ConditionalWeakTable<CharacterCreationMagicResonanceReReviewState, MagicReviewLoad> _magicReviewLoads = new();
    private readonly ConditionalWeakTable<CharacterCreationMagicResonanceReceipt, CharacterOverviewState> _magicReviewReceipts = new();

    internal bool CanDisplayCreationMagicReReview(CharacterCreationMagicResonanceReReviewState state) =>
        _magicReviewLoads.TryGetValue(state, out var issued)
        && IsCreationMagicOwnerCurrent(issued.Display.DisplayOwnerContext)
        && State.WorkspaceId == state.Binding.Current.WorkspaceId;

    internal bool IsCreationMagicReReviewCurrent(CharacterCreationMagicResonanceReReviewState state) =>
        _magicReviewLoads.TryGetValue(state, out var issued) && IsCreationCatalogDisplayCurrent(issued.Display)
        && CreationMagicReReviewPhoneAuthority.Matches(state, State);

    internal bool CanDisplayCreationMagicReReviewReceipt(CharacterCreationMagicResonanceReceipt receipt) =>
        _magicReviewReceipts.TryGetValue(receipt, out var original)
        && IsCreationMagicOwnerCurrent(original.DisplayOwnerContext) && State.WorkspaceId == receipt.WorkspaceId;

    internal CharacterCreationFoundationResult<CharacterCreationMagicResonanceReReviewState> LoadCreationMagicReReview()
    {
        var before = State;
        if (!IsCreationCatalogDisplayCurrent(before) || before.WorkspaceId is not { } id
            || before.DisplayOwnerContext is not { } owner
            || _ownerBoundMagicResonanceService is not IOwnerBoundCharacterCreationMagicResonanceReReviewService service)
            return MagicReviewUnavailable();
        var result = service.LoadReReview(owner, new(id));
        if (!IsCreationCatalogDisplayCurrent(before) || result.Value is { } candidate
            && (!CreationMagicReReviewPhoneAuthority.Matches(candidate, before)
                || !CreationMagicReReviewPhoneAuthority.Matches(candidate, State)))
            return MagicReviewUnavailable();
        if (result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } state })
            _magicReviewLoads.GetValue(state, _ => new(before, state));
        return result;
    }

    internal Task<CreationMagicReReviewConfirmResult> ConfirmCreationMagicReReviewAsync(
        CharacterCreationMagicResonanceReReviewState state, bool explicitlyReviewed,
        CancellationToken token = default) => WithWorkspaceActivationGateAsync(async () =>
    {
        if (!explicitlyReviewed || !_magicReviewLoads.TryGetValue(state, out var issued)
            || issued.ConfirmationStarted || !IsCreationMagicReReviewCurrent(state)
            || issued.Display.DisplayOwnerContext is not { } owner
            || _ownerBoundMagicResonanceService is not IOwnerBoundCharacterCreationMagicResonanceReReviewService service)
            return RejectedMagicReview();
        var selections = state.HistoricalDraft.Selections;
        var canonical = await Task.Run(() => service.PreviewReReview(owner, new(state.Binding, selections)), token);
        if (!IsCreationMagicReReviewCurrent(state)
            || canonical is not { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } exact }
            || !CreationMagicReReviewPhoneAuthority.ValidPreview(state, exact)
            || !CreationMagicReReviewPhoneAuthority.Equal(exact, state.InitialPreview))
            return RejectedMagicReview();
        string key = CreationMagicReReviewPhoneAuthority.IdempotencyKey(exact);
        token.ThrowIfCancellationRequested();
        issued.ConfirmationStarted = true;
        int entered = 0;
        CharacterCreationFoundationResult<CharacterCreationMagicResonanceReceipt> result;
        try
        {
            result = await Task.Run(() =>
            {
                Interlocked.Exchange(ref entered, 1);
                return service.ConfirmReReview(owner, new(exact.Binding, selections, exact.PreviewDigest, key, true, true));
            }, token);
        }
        catch (OperationCanceledException) when (Volatile.Read(ref entered) == 0)
        { issued.ConfirmationStarted = false; throw; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { return new("outcome-unknown", null, [CreationMagicResonancePhoneBlockers.OutcomeUnknown]); }
        if (result is not { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } receipt })
            return new(result.Outcome, null, result.Blockers);
        if (!CreationMagicReReviewPhoneAuthority.ReceiptMatches(receipt, exact, key))
            return new("outcome-unknown", null, [CreationMagicResonancePhoneBlockers.OutcomeUnknown]);
        _magicReviewReceipts.GetValue(receipt, _ => issued.Display);
        try
        {
            var loaded = await Task.Run(() => _ownerBoundMagicResonanceService.Load(owner, new(receipt.WorkspaceId)));
            if (loaded.Value is not { } saved || !CreationMagicReReviewPhoneAuthority.SavedMatches(receipt, exact, saved, key)
                || !IsCreationMagicOwnerCurrent(owner) || _presenter is not IOwnerBoundWorkspaceRefreshPresenter bound)
                return NeedsReopen();
            await bound.LoadAsync(owner, receipt.WorkspaceId, token);
            if (!CanDisplayCreationMagicReReviewReceipt(receipt)) return NeedsReopen();
            await SyncShellAsync(token);
            if (!CanDisplayCreationMagicReReviewReceipt(receipt)
                || State.CreationMagicResonance is not { } current
                || !CreationMagicReReviewPhoneAuthority.SavedMatches(receipt, exact, current, key)) return NeedsReopen();
            NotifyChanged();
            return new(CharacterCreationFoundationOutcomes.Success, receipt, []);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { return NeedsReopen(); }
        CreationMagicReReviewConfirmResult NeedsReopen() => new(CharacterCreationFoundationOutcomes.Success, receipt,
            [CreationMagicResonancePhoneBlockers.PostCommitRefreshRequired]);
    }, token);

    private static CharacterCreationFoundationResult<CharacterCreationMagicResonanceReReviewState> MagicReviewUnavailable() =>
        new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationMagicResonanceReReviewSchemas.Unavailable]);
    private static CreationMagicReReviewConfirmResult RejectedMagicReview() =>
        new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationMagicResonanceReReviewSchemas.Stale]);
}
