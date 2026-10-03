using System.Runtime.CompilerServices;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    private sealed class CreationPurchaseAttempt { public int Started; }
    private readonly ConditionalWeakTable<object, CreationPurchaseAttempt> _purchaseAttempts = new();

    internal bool CanDisplayCreationPurchase(CharacterOverviewState original)
        => original.DisplayOwnerContext is { IsValid: true } owner
           && IsNativePersistenceOwnerCurrent(owner) && State.DisplayOwnerContext == owner
           && State.Session.OwnerContext == owner && State.WorkspaceId == original.WorkspaceId;

    private async Task<bool> RefreshCreationPurchaseAsync(CharacterOverviewState original,
        CharacterWorkspaceId workspaceId, long revision, long savedRevision, CancellationToken ct)
    {
        if (!CanDisplayCreationPurchase(original) || original.DisplayOwnerContext is not { } owner
            || _presenter is not IOwnerBoundWorkspaceRefreshPresenter bound)
            return false;
        // Android performs the owner-bound shell sync below. Loading with the
        // presenter's default also lists/reopens the entire roster, duplicating
        // that work after every Resources or Gear checkpoint.
        await bound.LoadBeforeShellSyncAsync(owner, workspaceId, ct);
        if (!CanDisplayCreationPurchase(original) || State.Error is not null
            || State.ContentRevision != revision || State.SavedRevision != savedRevision)
            return false;
        await SyncShellAsync(ct);
        return CanDisplayCreationPurchase(original) && State.ContentRevision == revision
            && State.SavedRevision == savedRevision && State.Error is null;
    }

    internal Task<CharacterCreationResourcesInteractionConfirmResult> ConfirmCreationResourcesPurchaseAsync(
        ICharacterCreationResourcesInteractionPresenter presenter, CharacterOverviewState original,
        CharacterCreationResourcesPreparedPreview prepared, CancellationToken ct = default)
        => WithWorkspaceActivationGateAsync(async () =>
        {
            // Capture the display before waiting. Core alone owns the synchronous
            // lease; a queued operation cannot borrow a later account generation.
            if (!IsCreationCatalogDisplayCurrent(original))
                return new CharacterCreationResourcesInteractionConfirmResult(
                    CharacterCreationResourcesOutcomes.Conflict, prepared, null, null,
                    [CharacterCreationResourcesInteractionBlockers.OverviewAuthorityRequired]);
            var attempt = _purchaseAttempts.GetValue(prepared, _ => new());
            if (Interlocked.CompareExchange(ref attempt.Started, 1, 0) != 0)
                return new CharacterCreationResourcesInteractionConfirmResult(
                    CharacterCreationResourcesOutcomes.Conflict, prepared, null, null,
                    ["creation-purchase-already-submitted"]);
            int entered = 0;
            CharacterCreationResourcesInteractionConfirmResult result;
            try
            {
                result = await Task.Run(() =>
                {
                    Interlocked.Exchange(ref entered, 1);
                    return presenter.Confirm(original, new(prepared, prepared.PreviewDigest,
                        prepared.IdempotencyKey, ExplicitlyConfirmed: true));
                }, ct);
            }
            catch (OperationCanceledException) when (Volatile.Read(ref entered) == 0)
            {
                Interlocked.Exchange(ref attempt.Started, 0);
                throw;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                return new CharacterCreationResourcesInteractionConfirmResult(
                    "outcome-unknown", prepared, null, null, ["creation-purchase-outcome-unknown"]);
            }
            if (result.Receipt is not { } receipt
                || result.Outcome is not (CharacterCreationResourcesOutcomes.Applied or CharacterCreationResourcesOutcomes.Replayed)
                || !CreationResourcesPhoneAuthority.ReceiptMatches(prepared, receipt))
                return result;
            try
            {
                if (!await RefreshCreationPurchaseAsync(original, receipt.WorkspaceId,
                        receipt.WorkspaceRevision, receipt.SavedRevision, ct))
                    return NeedsReopen();
                CharacterOverviewState refreshedDisplay = State;
                var reopened = await Task.Run(() => ReadCreationAuthority(refreshedDisplay,
                    () => presenter.Load(refreshedDisplay), ct), ct);
                if (reopened.State is not { } state
                    || !CreationResourcesPhoneAuthority.RefreshedStateMatches(prepared, receipt, state)
                    || !CanDisplayCreationPurchase(original))
                    return NeedsReopen();
                NotifyChanged();
                return result with { RefreshedState = state, Blockers = [] };
            }
            catch (Exception error) when (error is not OutOfMemoryException) { return NeedsReopen(); }
            CharacterCreationResourcesInteractionConfirmResult NeedsReopen() => result with
            {
                RefreshedState = null,
                Blockers = [CharacterCreationResourcesInteractionBlockers.RefreshAuthorityRequired]
            };
        }, ct);

    internal Task<CharacterCreationGearInteractionConfirmResult> ConfirmCreationGearPurchaseAsync(
        ICharacterCreationGearInteractionPresenter presenter, CharacterOverviewState original,
        CharacterCreationGearPreparedPreview prepared, CancellationToken ct = default)
        => WithWorkspaceActivationGateAsync(async () =>
        {
            // Capture the display before waiting. Core alone owns the synchronous
            // lease; a queued operation cannot borrow a later account generation.
            if (!IsCreationCatalogDisplayCurrent(original))
                return new CharacterCreationGearInteractionConfirmResult(
                    CharacterCreationGearOutcomes.Conflict, prepared, null, null,
                    [CharacterCreationGearInteractionBlockers.OverviewAuthorityRequired]);
            var attempt = _purchaseAttempts.GetValue(prepared, _ => new());
            if (Interlocked.CompareExchange(ref attempt.Started, 1, 0) != 0)
                return new CharacterCreationGearInteractionConfirmResult(
                    CharacterCreationGearOutcomes.Conflict, prepared, null, null,
                    ["creation-purchase-already-submitted"]);
            int entered = 0;
            CharacterCreationGearInteractionConfirmResult result;
            try
            {
                result = await Task.Run(() =>
                {
                    Interlocked.Exchange(ref entered, 1);
                    return presenter.Confirm(original, new(prepared, prepared.Preview.PreviewDigest,
                        prepared.IdempotencyKey, ExplicitlyConfirmed: true));
                }, ct);
            }
            catch (OperationCanceledException) when (Volatile.Read(ref entered) == 0)
            {
                Interlocked.Exchange(ref attempt.Started, 0);
                throw;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                return new CharacterCreationGearInteractionConfirmResult(
                    "outcome-unknown", prepared, null, null, ["creation-purchase-outcome-unknown"]);
            }
            if (result.Receipt is not { } receipt
                || result.Outcome is not (CharacterCreationGearOutcomes.Applied or CharacterCreationGearOutcomes.Replayed)
                || !CreationGearPhoneAuthority.ReceiptMatches(prepared, receipt))
                return result;
            try
            {
                if (!await RefreshCreationPurchaseAsync(original, receipt.WorkspaceId,
                        receipt.WorkspaceRevision, receipt.SavedRevision, ct))
                    return NeedsReopen();
                CharacterOverviewState refreshedDisplay = State;
                var reopened = await Task.Run(() => ReadCreationAuthority(refreshedDisplay,
                    () => presenter.Load(refreshedDisplay), ct), ct);
                if (reopened.State is not { } state
                    || !CreationGearPhoneAuthority.RefreshedStateMatches(prepared, receipt, state)
                    || !CanDisplayCreationPurchase(original))
                    return NeedsReopen();
                NotifyChanged();
                return result with { RefreshedState = state, Blockers = [] };
            }
            catch (Exception error) when (error is not OutOfMemoryException) { return NeedsReopen(); }
            CharacterCreationGearInteractionConfirmResult NeedsReopen() => result with
            {
                RefreshedState = null,
                Blockers = [CharacterCreationGearInteractionBlockers.RefreshAuthorityRequired]
            };
        }, ct);
}
