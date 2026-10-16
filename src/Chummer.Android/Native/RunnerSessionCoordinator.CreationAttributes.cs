using System.Runtime.CompilerServices;
using Chummer.Contracts.Characters;
using Chummer.Presentation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    internal T ReadCreationAuthority<T>(CharacterOverviewState original, Func<T> read, CancellationToken ct)
    {
        if (!AttributesDisplayCurrent(original) || original.DisplayOwnerContext is not { } owner)
            throw new InvalidOperationException("The original Creation display is no longer current.");
        T result = _damageJournalOwnerAccessor is AndroidAccountOwnerContextAccessor androidOwner
            ? androidOwner.RunScheduledRead(owner, read, ct) : read();
        ct.ThrowIfCancellationRequested();
        if (!AttributesDisplayCurrent(original))
            throw new InvalidOperationException("The Creation owner changed during its read.");
        return result;
    }

    private sealed record AttributesLoad(CharacterOverviewState Display, CharacterCreationAttributesState State);
    private sealed record AttributesPreview(AttributesLoad Load, CharacterCreationAttributeAllocation[] Allocations,
        string[]? PlayerChosenAttributeIds, CharacterCreationFoundationResult<CharacterCreationAttributesPreview> Result)
    {
        public bool ConfirmationStarted { get; set; }
    }

    private readonly ConditionalWeakTable<CharacterCreationAttributesBinding, AttributesLoad> _attributeLoads = new();
    private readonly ConditionalWeakTable<CharacterCreationAttributesPreview, AttributesPreview> _attributePreviews = new();
    private readonly ConditionalWeakTable<CharacterCreationAttributesReceipt, CharacterOverviewState> _attributeReceipts = new();

    private static CharacterCreationFoundationResult<T> AttributesUnavailable<T>() where T : class
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationAttributesBlockers.WorkspaceUnavailable]);

    private bool AttributesDisplayCurrent(CharacterOverviewState original)
        => original.Profile?.Created == false && original.DisplayOwnerContext is { IsValid: true }
           && original.Session.OwnerContext == original.DisplayOwnerContext && IsNativeEditDisplayCurrent(original);

    internal bool IsCreationAttributesStateCurrent(CharacterCreationAttributesState state)
        => _attributeLoads.TryGetValue(state.Binding, out var issued)
           && ReferenceEquals(issued.State, state) && AttributesDisplayCurrent(issued.Display)
           && CreationAttributesPhoneAuthority.MatchesOverview(state, State);

    // Cheap lifetime check for an already detached Core draft. No save capability
    // is issued here; fresh Preview/Confirm retain their full source/store checks.
    internal bool IsCreationAttributesDraftDisplayCurrent(CharacterCreationAttributesState state)
        => _attributeLoads.TryGetValue(state.Binding, out var issued)
           && ReferenceEquals(issued.State, state) && AttributesDisplayCurrent(issued.Display);

    public CharacterCreationFoundationResult<CharacterCreationAttributesState> LoadCreationAttributes()
        => LoadCreationAttributesForDisplay(State);

    private CharacterCreationFoundationResult<CharacterCreationAttributesState> LoadCreationAttributesForDisplay(
        CharacterOverviewState original)
    {
        if (!AttributesDisplayCurrent(original) || original.WorkspaceId is not { } id
            || original.DisplayOwnerContext is not { } owner || _ownerBoundAttributesService is not { } service)
            return AttributesUnavailable<CharacterCreationAttributesState>();
        var result = service.Load(owner, new(id));
        if (!AttributesDisplayCurrent(original)
            || result.Value is { } candidate && !CreationAttributesPhoneAuthority.MatchesOverview(candidate, State))
            return AttributesUnavailable<CharacterCreationAttributesState>();
        if (result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } state })
        {
            var issued = _attributeLoads.GetValue(state.Binding, _ => new(original, state));
            if (!ReferenceEquals(issued.State, state) || !AttributesDisplayCurrent(issued.Display))
                return AttributesUnavailable<CharacterCreationAttributesState>();
        }
        return result;
    }

    internal CharacterCreationFoundationResult<CharacterCreationAttributesState> RevalidateCreationAttributes(
        CharacterCreationAttributesState issued)
        => _attributeLoads.TryGetValue(issued.Binding, out var load) && ReferenceEquals(load.State, issued)
            ? LoadCreationAttributesForDisplay(load.Display) : AttributesUnavailable<CharacterCreationAttributesState>();

    // Read-only page re-entry after another wizard step saved. This issues a
    // NEW Core snapshot; it does not revive any old preview or confirmation.
    internal CharacterCreationFoundationResult<CharacterCreationAttributesState> ReloadCreationAttributesForPage(
        CharacterCreationAttributesState issued, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = State;
        if (!_attributeLoads.TryGetValue(issued.Binding, out var load) || !ReferenceEquals(load.State, issued)
            || load.Display.WorkspaceId != current.WorkspaceId
            || load.Display.DisplayOwnerContext != current.DisplayOwnerContext
            || !IsNativePersistenceOwnerCurrent(load.Display.DisplayOwnerContext)
            || !AttributesDisplayCurrent(current))
            return AttributesUnavailable<CharacterCreationAttributesState>();
        return ReadCreationAuthority(current, () => LoadCreationAttributesForDisplay(current), cancellationToken);
    }

    internal CharacterCreationFoundationResult<CharacterCreationAttributesPreview> PreviewCreationAttributes(
        CharacterCreationAttributesBinding binding, IReadOnlyList<CharacterCreationAttributeAllocation> allocations,
        IReadOnlyList<string>? playerChosenAttributeIds = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(allocations);
        if (!_attributeLoads.TryGetValue(binding, out var issued) || !IsCreationAttributesStateCurrent(issued.State)
            || !CreationAttributesPhoneAuthority.IsReady(issued.State, State)
            || issued.Display.DisplayOwnerContext is not { } owner || _ownerBoundAttributesService is not { } service)
            return AttributesUnavailable<CharacterCreationAttributesPreview>();
        var copied = allocations.ToArray();
        var copiedIds = playerChosenAttributeIds?.ToArray();
        // Core Preview already reads the current workspace under the exact owner
        // lease, rebuilds its source-bound state and checks the complete binding.
        // A separate Load here repeats that expensive work for every +/- option.
        // Keep the issued-display guards; confirmation still reloads/reprojects.
        var result = service.Preview(owner, new(binding, copied) { PlayerChosenAttributeIds = copiedIds });
        if (!IsCreationAttributesStateCurrent(issued.State)) return AttributesUnavailable<CharacterCreationAttributesPreview>();
        // Core can also issue the exact saved projection as a blocked duplicate.
        // Admit it for read-only review through the existing strict local-undo
        // checks, never by removing its blocker or making it confirmable.
        if (result.Value is { } preview
            && CreationAttributesPhoneAuthority.CanAdoptPreview(issued.State, State, result, copied, copiedIds))
            _attributePreviews.GetValue(preview, _ => new(issued, copied, copiedIds?.ToArray(), result));
        return result;
    }

    internal bool IsCreationAttributesPreviewCurrent(CharacterCreationAttributesPreview preview)
        => _attributePreviews.TryGetValue(preview, out var issued) && !issued.ConfirmationStarted
           && IsCreationAttributesStateCurrent(issued.Load.State);

    internal bool CanOfferCreationAttributesReview(CharacterCreationAttributesPreview preview,
        IReadOnlyList<CharacterCreationAttributeAllocation> allocations)
        => _attributePreviews.TryGetValue(preview, out var issued)
           && IsCreationAttributesPreviewCurrent(preview)
           && allocations.SequenceEqual(issued.Allocations)
           && CreationAttributesPhoneAuthority.PlayerChosenIdsEqual(preview.PlayerChosenAttributeIds, issued.PlayerChosenAttributeIds)
           && CreationAttributesPhoneAuthority.CanAdoptPreview(issued.Load.State, State, issued.Result, allocations,
               issued.PlayerChosenAttributeIds);

    internal bool IsSavedCreationAttributesReview(CharacterCreationAttributesPreview preview,
        IReadOnlyList<CharacterCreationAttributeAllocation> allocations)
        => !preview.CanConfirm && CanOfferCreationAttributesReview(preview, allocations);

    // Rendering uses the exact issued projection, never a synchronous Core read.
    // This only offers the review action: ConfirmIssuedAttributesAsync still
    // reloads, reprojects and validates the current bytes before any mutation.
    internal bool CanOfferCreationAttributesConfirmation(CharacterCreationAttributesPreview preview,
        IReadOnlyList<CharacterCreationAttributeAllocation> allocations)
        => _attributePreviews.TryGetValue(preview, out var issued)
           && IsCreationAttributesPreviewCurrent(preview)
           && allocations.SequenceEqual(issued.Allocations)
           && CreationAttributesPhoneAuthority.PlayerChosenIdsEqual(preview.PlayerChosenAttributeIds, issued.PlayerChosenAttributeIds)
           && CreationAttributesPhoneAuthority.CanConfirmPreview(issued.Load.State, State, preview, allocations,
               issued.PlayerChosenAttributeIds);

    internal bool CanDisplayCreationAttributesPreview(CharacterCreationAttributesPreview preview)
        => _attributePreviews.TryGetValue(preview, out var issued)
           // A successful confirmation may retain its original preview alongside
           // the receipt. A saved-only review has no such transition: it must
           // remain bound to the exact current saved revision and projection.
           && (issued.Result.Outcome == CharacterCreationFoundationOutcomes.Success
               || CanOfferCreationAttributesReview(preview, issued.Allocations))
           && IsNativePersistenceOwnerCurrent(issued.Load.Display.DisplayOwnerContext)
           && State.WorkspaceId == issued.Load.Display.WorkspaceId
           && State.DisplayOwnerContext == issued.Load.Display.DisplayOwnerContext
           && State.Session.OwnerContext == issued.Load.Display.DisplayOwnerContext;

    internal bool IsCreationAttributesReceiptCurrent(CharacterCreationAttributesReceipt receipt)
        => _attributeReceipts.TryGetValue(receipt, out var original)
           && IsNativePersistenceOwnerCurrent(original.DisplayOwnerContext)
           && State.DisplayOwnerContext == original.DisplayOwnerContext && State.Session.OwnerContext == original.DisplayOwnerContext
           && State.Error is null && State.WorkspaceId == receipt.WorkspaceId
           && State.ContentRevision == receipt.ContentRevision && State.SavedRevision == receipt.SavedRevision;

    internal Task<CreationAttributesPhoneConfirmResult> ConfirmCreationAttributesAsync(
        CharacterCreationAttributesPreview preview, IReadOnlyList<CharacterCreationAttributeAllocation> allocations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(allocations);
        var copied = allocations.ToArray();
        return WithWorkspaceActivationGateAsync(() => ConfirmIssuedAttributesAsync(preview, copied, cancellationToken), cancellationToken);
    }

    private async Task<CreationAttributesPhoneConfirmResult> ConfirmIssuedAttributesAsync(
        CharacterCreationAttributesPreview preview, CharacterCreationAttributeAllocation[] allocations, CancellationToken ct)
    {
        if (!_attributePreviews.TryGetValue(preview, out var issued) || !CanOfferCreationAttributesConfirmation(preview, allocations)
            || !allocations.SequenceEqual(issued.Allocations) || _ownerBoundAttributesService is not { } service
            || issued.Load.Display.DisplayOwnerContext is not { } owner)
            return RejectedAttributes(CharacterCreationAttributesBlockers.StaleWorkspaceRevision);
        var original = issued.Load.Display;
        var live = await Task.Run(() => service.Load(owner, new(preview.Binding.WorkspaceId)), ct);
        if (!IsCreationAttributesPreviewCurrent(preview) || live.Value is not { } state
            || !CreationAttributesPhoneAuthority.CanConfirmPreview(state, original, preview, allocations,
                issued.PlayerChosenAttributeIds))
            return RejectedAttributes(CharacterCreationAttributesBlockers.PreviewDigestMismatch);
        var reprojection = await Task.Run(() => service.Preview(owner, new(preview.Binding, allocations)
            { PlayerChosenAttributeIds = issued.PlayerChosenAttributeIds?.ToArray() }), ct);
        if (!IsCreationAttributesPreviewCurrent(preview)
            || !CreationAttributesPhoneAuthority.CanAdoptPreview(state, original, reprojection, allocations,
                issued.PlayerChosenAttributeIds)
            || reprojection.Value is not { } canonical || !CreationAttributesPhoneAuthority.CanonicallyEquals(preview, canonical))
            return RejectedAttributes(CharacterCreationAttributesBlockers.PreviewDigestMismatch);

        ct.ThrowIfCancellationRequested();
        issued.ConfirmationStarted = true;
        int entered = 0;
        CharacterCreationFoundationResult<CharacterCreationAttributesReceipt> result;
        try
        {
            // Core acquires and releases the exact original owner's lease on this
            // worker, including the atomic checkpoint; no lease crosses an await.
            result = await Task.Run(() =>
            {
                Interlocked.Exchange(ref entered, 1);
                return service.Confirm(owner, new(canonical.Binding, allocations, canonical.PreviewDigest, true)
                    { PlayerChosenAttributeIds = issued.PlayerChosenAttributeIds?.ToArray() });
            }, ct);
        }
        catch (OperationCanceledException) when (Volatile.Read(ref entered) == 0)
        { issued.ConfirmationStarted = false; throw; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // There is no operation-ID lookup for Attributes. Do not retry an
            // uncertain write or present it as a known failure without a commit.
            return new("outcome-unknown", null, null, ["creation-attributes-confirm-outcome-unknown"]);
        }
        if (result is not { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } receipt })
            return new(result.Outcome, result.Value, null, result.Blockers);
        _attributeReceipts.GetValue(receipt, _ => original);
        try
        {
            var committed = await Task.Run(() => service.Load(owner, new(receipt.WorkspaceId)));
            if (committed.Value is not { } saved
                || !CreationAttributesPhoneAuthority.ReceiptMatchesBeforeActivation(receipt, canonical, allocations, saved, original)
                || !IsNativePersistenceOwnerCurrent(owner)
                || _presenter is not IOwnerBoundWorkspaceRefreshPresenter bound)
                return NeedsReopen();
            await bound.LoadAsync(owner, receipt.WorkspaceId, ct);
            if (!IsCreationAttributesReceiptCurrent(receipt)) return NeedsReopen();
            await SyncShellAsync(ct);
            if (!IsCreationAttributesReceiptCurrent(receipt)) return NeedsReopen();
            var currentDisplay = State;
            var refreshed = await Task.Run(() => ReadCreationAuthority(currentDisplay,
                LoadCreationAttributes, ct), ct);
            if (refreshed.Value is not { } fresh || !IsCreationAttributesStateCurrent(fresh)
                || !CreationAttributesPhoneAuthority.ReceiptMatches(receipt, canonical, allocations, fresh, State))
                return NeedsReopen();
            _notice = "Attributes draft saved. Character effects remain pending finalization.";
            NotifyChanged();
            return IsCreationAttributesReceiptCurrent(receipt)
                ? new(CharacterCreationFoundationOutcomes.Success, receipt, fresh, []) : NeedsReopen();
        }
        catch (Exception error) when (error is not OutOfMemoryException) { return NeedsReopen(); }
        CreationAttributesPhoneConfirmResult NeedsReopen() => new(CharacterCreationFoundationOutcomes.Success,
            receipt, null, ["creation-attributes-post-commit-refresh-required"]);
    }

    private static CreationAttributesPhoneConfirmResult RejectedAttributes(string blocker)
        => new(CharacterCreationFoundationOutcomes.Conflict, null, null, [blocker]);
}
