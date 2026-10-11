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

    internal Task<CharacterCreationLifestylesInteractionLoadResult> LoadCreationLifestylesForDisplayAsync(
        CharacterOverviewState original, CancellationToken ct)
        => WithWorkspaceActivationGateAsync(() => Task.Run(() =>
            ReadCreationAuthority(original, () => _creationLifestylesPresenter.Load(original), ct), ct), ct);

    internal Task<CharacterCreationLifestylesInteractionPrepareResult> PrepareCreationLifestyleForDisplayAsync(
        CharacterOverviewState original, CharacterCreationLifestyleMutationInput input, CancellationToken ct)
        => WithWorkspaceActivationGateAsync(() => Task.Run(() =>
            ReadCreationAuthority(original, () => _creationLifestylesPresenter.Prepare(original, input), ct), ct), ct);

    internal async Task<CreationContactsPage?> PrepareCreationAfterContactPageAsync(
        CharacterOverviewState original, CharacterCreationContactPreparedPreview prepared,
        CharacterCreationContactReceipt receipt, CharacterCreationContactsInteractionState? refreshed,
        CancellationToken ct)
    {
        if (!CanDisplayCreationPurchase(original)
            || original.DisplayOwnerContext != prepared.DisplayOwnerContext
            || !CreationContactsPhoneAuthority.ReceiptMatches(prepared, receipt)) return null;
        if (refreshed is null || !CreationContactsPhoneAuthority.RefreshedStateMatches(prepared, receipt, refreshed, State))
        {
            refreshed = await WithWorkspaceActivationGateAsync(async () =>
            {
                if (!CanDisplayCreationPurchase(original)
                    || !await RefreshCreationPurchaseAsync(original, receipt.WorkspaceId,
                        receipt.ContentRevision, receipt.SavedRevision, ct)) return null;
                var display = State;
                var loaded = await Task.Run(() => ReadCreationAuthority(display,
                    () => _creationContactsPresenter.Load(display), ct), ct);
                return loaded.Outcome == CharacterCreationContactOutcomes.Available
                    && loaded.Blockers.Count == 0 ? loaded.State : null;
            }, ct);
        }
        ct.ThrowIfCancellationRequested();
        var current = State;
        return refreshed is not null && CanDisplayCreationPurchase(original)
            && IsCreationCatalogDisplayCurrent(current)
            && CreationContactsPhoneAuthority.RefreshedStateMatches(prepared, receipt, refreshed, current)
            && CreationContactsPhoneAuthority.IsReady(refreshed, current)
                ? new CreationContactsPage(this, refreshed, current, null, receipt) : null;
    }


    internal async Task<CreationLifestylesPage?> PrepareCreationAfterLifestylePageAsync(
        CharacterOverviewState original, CharacterCreationLifestylePreparedPreview prepared,
        CharacterCreationLifestyleReceipt receipt, CharacterCreationLifestylesInteractionState? refreshed,
        CancellationToken ct)
    {
        if (!CanDisplayCreationPurchase(original)
            || original.DisplayOwnerContext != prepared.DisplayOwnerContext
            || !CreationLifestylesPhoneAuthority.ReceiptMatches(prepared, receipt)) return null;
        if (refreshed is null || !CreationLifestylesPhoneAuthority.RefreshedStateMatches(prepared, receipt, refreshed, State))
        {
            refreshed = await WithWorkspaceActivationGateAsync(async () =>
            {
                if (!CanDisplayCreationPurchase(original)
                    || !await RefreshCreationPurchaseAsync(original, receipt.WorkspaceId,
                        receipt.ContentRevision, receipt.SavedRevision, ct)) return null;
                var display = State;
                var loaded = await Task.Run(() => ReadCreationAuthority(display,
                    () => _creationLifestylesPresenter.Load(display), ct), ct);
                return loaded.Outcome == CharacterCreationLifestyleOutcomes.Available
                    && loaded.Blockers.Count == 0 ? loaded.State : null;
            }, ct);
        }
        ct.ThrowIfCancellationRequested();
        var current = State;
        return refreshed is not null && CanDisplayCreationPurchase(original)
            && IsCreationCatalogDisplayCurrent(current)
            && CreationLifestylesPhoneAuthority.RefreshedStateMatches(prepared, receipt, refreshed, current)
            && CreationLifestylesPhoneAuthority.IsReady(refreshed, current)
                ? new CreationLifestylesPage(this, refreshed, current) : null;
    }

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

    internal async Task<CreationGearPage?> PrepareCreationAfterResourcesPageAsync(
        ICharacterCreationResourcesInteractionPresenter resources, CharacterOverviewState original,
        CharacterCreationResourcesPreparedPreview prepared, CharacterCreationResourcesReceipt receipt,
        CancellationToken ct)
    {
        if (_creationGearPresenter is null || !CanDisplayCreationPurchase(original)
            || !CreationResourcesPhoneAuthority.ReceiptMatches(prepared, receipt)) return null;
        var display = await WithWorkspaceActivationGateAsync(async () =>
        {
            if (!CanDisplayCreationPurchase(original)) return null;
            // Reopening a known commit is read-only. It never repeats Confirm,
            // even if the previous post-commit overview refresh failed.
            if (State.ContentRevision != receipt.WorkspaceRevision
                || State.SavedRevision != receipt.SavedRevision || State.Error is not null)
                if (!await RefreshCreationPurchaseAsync(original, receipt.WorkspaceId,
                        receipt.WorkspaceRevision, receipt.SavedRevision, ct)) return null;
            var current = State;
            if (!IsCreationCatalogDisplayCurrent(current)) return null;
            bool exact = await Task.Run(() => ReadCreationAuthority(current, () =>
                resources.Load(current).State is { } state
                && CreationResourcesPhoneAuthority.RefreshedStateMatches(prepared, receipt, state), ct), ct);
            return exact && CanDisplayCreationPurchase(original) && IsCreationCatalogDisplayCurrent(current)
                ? current : null;
        }, ct);
        if (display is null) return null;
        var gear = await LoadCreationGearForDisplayAsync(_creationGearPresenter, display, ct);
        ct.ThrowIfCancellationRequested();
        return gear.Ready && gear.Load.Outcome == CharacterCreationGearOutcomes.Available
            && CanDisplayCreationPurchase(original) && IsCreationCatalogDisplayCurrent(display)
                ? new CreationGearPage(this, _creationGearPresenter, _presenter, display, gear.Load, receipt) : null;
    }

    internal sealed record CreationGearContinuation(Page Page, CharacterOverviewState Display);

    internal async Task<CreationGearContinuation?> PrepareCreationAfterGearPageAsync(
        ICharacterCreationGearInteractionPresenter gear, CharacterOverviewState original,
        CharacterCreationGearPreparedPreview prepared, CharacterCreationGearReceipt receipt,
        CancellationToken ct)
    {
        if (!CanDisplayCreationPurchase(original)
            || !CreationGearPhoneAuthority.ReceiptMatches(prepared, receipt)) return null;
        var display = await WithWorkspaceActivationGateAsync(async () =>
        {
            if (!CanDisplayCreationPurchase(original)) return null;
            // Observe the known saved basket; never repeat a purchase when only
            // its post-commit refresh or the next page's read failed.
            if (State.ContentRevision != receipt.WorkspaceRevision
                || State.SavedRevision != receipt.SavedRevision || State.Error is not null)
                if (!await RefreshCreationPurchaseAsync(original, receipt.WorkspaceId,
                        receipt.WorkspaceRevision, receipt.SavedRevision, ct)) return null;
            var current = State;
            if (!IsCreationCatalogDisplayCurrent(current)) return null;
            bool exact = await Task.Run(() => ReadCreationAuthority(current, () =>
                gear.Load(current).State is { } state
                && CreationGearPhoneAuthority.RefreshedStateMatches(prepared, receipt, state), ct), ct);
            return exact && CanDisplayCreationPurchase(original) && IsCreationCatalogDisplayCurrent(current)
                ? current : null;
        }, ct);
        if (display is null) return null;
        return await PrepareCreationGearNextStepAsync(display, receipt, ct);
    }

    internal async Task<CreationGearContinuation?> PrepareSavedCreationGearNextPageAsync(
        ICharacterCreationGearInteractionPresenter gear, CharacterOverviewState display,
        CharacterCreationGearInteractionState issued, CancellationToken ct)
    {
        if (!IsCreationCatalogDisplayCurrent(display) || issued.PendingDraft is null) return null;
        var reopened = await LoadCreationGearForDisplayAsync(gear, display, ct);
        if (!reopened.Ready || reopened.Load.Outcome != CharacterCreationGearOutcomes.Available
            || reopened.Load.State is not { PendingDraft: not null } current
            || current.Binding != issued.Binding
            || !CharacterCreationGearRules.DigestsEqual(current.SnapshotDigest, issued.SnapshotDigest)
            || !IsCreationCatalogDisplayCurrent(display)) return null;
        return await PrepareCreationGearNextStepAsync(display, null, ct);
    }

    private async Task<CreationGearContinuation?> PrepareCreationGearNextStepAsync(
        CharacterOverviewState display, CharacterCreationGearReceipt? receipt, CancellationToken ct)
    {
        // This loader has its own activation gate: call after releasing the
        // refresh gate. Contacts keeps Core's actual shared-budget admission.
        var contacts = await LoadCreationContactsForDisplayAsync(display, ct);
        ct.ThrowIfCancellationRequested();
        if (contacts.Outcome != CharacterCreationContactOutcomes.Available
            || contacts.State is not { } state || !CreationContactsPhoneAuthority.IsBound(state, display)
            || !IsCreationCatalogDisplayCurrent(display)) return null;
        if (CreationContactsPhoneAuthority.IsReady(state, display))
            return new(new CreationContactsPage(this, state, display, receipt), display);

        // Gear can be saved after visiting steps out of order. A Contacts budget
        // denied by Core is not a transient read failure: finish the missing
        // Skills choices instead of sending the user through an endless retry.
        // Only the exact current wizard and fresh, owner-bound Skills authority
        // admit this route. Never manufacture contact points or save a draft.
        if (!state.Blockers.Contains(CharacterCreationContactsBlockers.BudgetAuthorityRequired)
            || display.CreationWizard is not { } wizard
            || wizard.WorkspaceId != display.WorkspaceId?.Value
            || wizard.WorkspaceRevision != display.ContentRevision
            || !wizard.CompletionBlockers.Contains(CharacterCreationFinalizationBlockers.SkillsDraftRequired))
            return null;
        var skills = await WithWorkspaceActivationGateAsync(() => Task.Run(() =>
            ReadCreationAuthority(display, () => LoadCreationSkillsForDisplay(display), ct), ct), ct);
        ct.ThrowIfCancellationRequested();
        return skills.Outcome == CharacterCreationFoundationOutcomes.Success
            && skills.Value is { } requiredSkills && CreationSkillsPhoneAuthority.IsReady(requiredSkills, display)
            && IsCreationSkillsStateCurrent(requiredSkills)
            && IsCreationCatalogDisplayCurrent(display)
                ? new(new CreationSkillsPage(this, requiredSkills, afterSavedGear: true), display) : null;
    }

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
