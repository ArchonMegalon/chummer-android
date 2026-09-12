using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Chummer.Contracts.Characters;
using Chummer.Presentation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

// The dashboard's existing background queue carries its scheduling-time frame back
// to the UI. Only acceptance, never the worker, registers an issued authority.
internal sealed record CreationPrerequisitePhoneLoad(
    CharacterOverviewState OriginalDisplay,
    CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> Result);

public sealed partial class RunnerSessionCoordinator
{
    internal const string PrerequisitePostCommitRefreshRequired =
        "creation-prerequisite-post-commit-refresh-required";
    internal const string PrerequisiteOutcomeUnknown =
        "creation-prerequisite-confirm-outcome-unknown";
    internal const string PrerequisiteConfirmationAlreadyAttempted =
        "creation-prerequisite-confirm-already-attempted";

    private sealed record PrerequisiteLoadIssuance(
        CharacterOverviewState OriginalDisplay, CharacterCreationPrerequisiteState State);

    private sealed record PrerequisitePreviewIssuance(
        PrerequisiteLoadIssuance Load,
        IReadOnlyDictionary<string, string> Assignments,
        CreationPrerequisitePhoneSelections Selections)
    {
        // Accessed only at the coordinator's admitted/UI continuation boundary.
        public bool ConfirmationStarted { get; set; }
    }

    private readonly ConditionalWeakTable<CharacterCreationPrerequisiteState, CharacterOverviewState>
        _prerequisiteStates = new();
    private readonly ConditionalWeakTable<CharacterCreationPrerequisiteBinding, PrerequisiteLoadIssuance>
        _prerequisiteLoads = new();
    private readonly ConditionalWeakTable<CharacterCreationPrerequisitePreview, PrerequisitePreviewIssuance>
        _prerequisitePreviews = new();
    private readonly ConditionalWeakTable<CharacterCreationPrerequisiteReceipt, CharacterOverviewState>
        _prerequisiteReceipts = new();
    private CharacterCreationPrerequisiteState? _prerequisiteCachedState;

    private static CharacterCreationFoundationResult<T> PrerequisiteUnavailable<T>() where T : class
        => new(CharacterCreationFoundationOutcomes.Blocked, null,
            [CharacterCreationPrerequisiteBlockers.WorkspaceUnavailable]);

    private static CharacterCreationFoundationResult<T> PrerequisiteStale<T>() where T : class
        => new(CharacterCreationFoundationOutcomes.Conflict, null,
            [CharacterCreationPrerequisiteBlockers.StaleWorkspaceRevision]);

    private bool IsCreationPrerequisiteDisplayCurrent(CharacterOverviewState original)
        => original.Profile?.Created == false && original.DisplayOwnerContext is { IsValid: true }
           && original.Session.OwnerContext == original.DisplayOwnerContext
           && IsNativeEditDisplayCurrent(original);

    internal bool IsCreationPrerequisiteStateCurrent(CharacterCreationPrerequisiteState state)
        => _prerequisiteStates.TryGetValue(state, out var original)
           && IsCreationPrerequisiteDisplayCurrent(original)
           && _prerequisiteLoads.TryGetValue(state.Binding, out var issued)
           && IsCreationPrerequisiteDisplayCurrent(issued.OriginalDisplay)
           && CreationPrerequisitePhoneAuthority.MatchesOverview(state, State)
           && _prerequisiteCachedState is { } current
           && CreationPrerequisitePhoneAuthority.BindingEquals(state.Binding, current.Binding)
           && state.SnapshotDigest == current.SnapshotDigest;

    internal CreationPrerequisitePhoneLoad LoadCreationPrerequisiteInBackground(
        CharacterOverviewState original)
    {
        // No State, presenter, Shell or UI access here. Core's synchronous adapter
        // acquires and disposes its genuine owner lease on this worker thread.
        var result = original.Profile?.Created == false
                     && original.WorkspaceId is { } workspaceId
                     && original.DisplayOwnerContext is { IsValid: true } owner
                     && original.Session.OwnerContext == owner
                     && _ownerBoundPrerequisiteService is { } service
            ? service.Load(owner, new(workspaceId))
            : PrerequisiteUnavailable<CharacterCreationPrerequisiteState>();
        return new(original, result);
    }

    internal CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>
        AcceptCreationPrerequisiteLoad(CreationPrerequisitePhoneLoad loaded)
    {
        if (!IsCreationPrerequisiteDisplayCurrent(loaded.OriginalDisplay)
            || loaded.Result.Value is { } candidate
               && !CreationPrerequisitePhoneAuthority.MatchesOverview(candidate, State))
            return PrerequisiteStale<CharacterCreationPrerequisiteState>();
        if (loaded.Result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } state })
        {
            var stateOwner = _prerequisiteStates.GetValue(state, _ => loaded.OriginalDisplay);
            var bindingOwner = _prerequisiteLoads.GetValue(state.Binding,
                _ => new(loaded.OriginalDisplay, state));
            if (!IsCreationPrerequisiteDisplayCurrent(stateOwner)
                || !IsCreationPrerequisiteDisplayCurrent(bindingOwner.OriginalDisplay))
                return PrerequisiteStale<CharacterCreationPrerequisiteState>();
            _prerequisiteCachedState = state;
        }
        else _prerequisiteCachedState = null;
        return loaded.Result;
    }

    public Task<CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>>
        LoadCreationPrerequisiteAsync(CancellationToken cancellationToken = default)
        => LoadCreationPrerequisiteAsync(State, cancellationToken);

    private Task<CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>>
        LoadCreationPrerequisiteAsync(CharacterOverviewState original, CancellationToken cancellationToken,
            Func<bool>? isCurrentPage = null)
        => WithWorkspaceActivationGateAsync(async () =>
        {
            if (isCurrentPage?.Invoke() == false || !IsCreationPrerequisiteDisplayCurrent(original))
                return PrerequisiteStale<CharacterCreationPrerequisiteState>();
            var loaded = await Task.Run(() => LoadCreationPrerequisiteInBackground(original), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (isCurrentPage?.Invoke() == false) return PrerequisiteStale<CharacterCreationPrerequisiteState>();
            return AcceptCreationPrerequisiteLoad(loaded);
        }, cancellationToken);

    internal Task<CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>>
        RevalidateCreationPrerequisiteAsync(CharacterCreationPrerequisiteState issued,
            CancellationToken cancellationToken = default, Func<bool>? isCurrentPage = null)
        => _prerequisiteStates.TryGetValue(issued, out var original)
            ? LoadCreationPrerequisiteAsync(original, cancellationToken, isCurrentPage)
            : Task.FromResult(PrerequisiteUnavailable<CharacterCreationPrerequisiteState>());

    private static IReadOnlyDictionary<string, string> CopyPrerequisiteAssignments(
        IReadOnlyDictionary<string, string> assignments)
        => new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(assignments, StringComparer.Ordinal));

    private static CreationPrerequisitePhoneSelections CopyPrerequisiteSelections(
        CreationPrerequisitePhoneSelections selections)
        => selections with
        {
            TalentActiveSkillSelectionIds = Array.AsReadOnly(selections.TalentActiveSkillSelectionIds.ToArray()),
            TalentSkillGroupSelectionIds = Array.AsReadOnly(selections.TalentSkillGroupSelectionIds.ToArray())
        };

    private Task<CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview>>
        PreviewIssuedCreationPrerequisiteAsync(CharacterCreationPrerequisiteBinding binding,
            IReadOnlyDictionary<string, string> assignments, CreationPrerequisitePhoneSelections selections,
            CancellationToken cancellationToken, Func<bool>? isCurrentEditor)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(selections);
        var copiedAssignments = CopyPrerequisiteAssignments(assignments);
        var copiedSelections = CopyPrerequisiteSelections(selections);
        if (!_prerequisiteLoads.TryGetValue(binding, out var issued))
            return Task.FromResult(PrerequisiteUnavailable<CharacterCreationPrerequisitePreview>());
        return WithWorkspaceActivationGateAsync(async () =>
        {
            if (isCurrentEditor?.Invoke() == false || !IsCreationPrerequisiteStateCurrent(issued.State)
                || !CreationPrerequisitePhoneAuthority.IsReady(issued.State, State)
                || _ownerBoundPrerequisiteService is not { } service
                || issued.OriginalDisplay.DisplayOwnerContext is not { IsValid: true } owner)
                return PrerequisiteStale<CharacterCreationPrerequisitePreview>();
            var request = new CharacterCreationPrerequisitePreviewRequest(binding, copiedAssignments)
            {
                HeritageSelectionId = copiedSelections.HeritageSelectionId,
                TalentSelectionId = copiedSelections.TalentSelectionId,
                TalentActiveSkillSelectionIds = copiedSelections.TalentActiveSkillSelectionIds,
                TalentSkillGroupSelectionIds = copiedSelections.TalentSkillGroupSelectionIds
            };
            var result = await Task.Run(() => service.Preview(owner, request), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (isCurrentEditor?.Invoke() == false || !IsCreationPrerequisiteStateCurrent(issued.State))
                return PrerequisiteStale<CharacterCreationPrerequisitePreview>();
            if (result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } preview })
            {
                if (!PreviewMatchesSelections(preview, copiedAssignments, copiedSelections, issued.State))
                    return PrerequisiteStale<CharacterCreationPrerequisitePreview>();
                _prerequisitePreviews.GetValue(preview, _ => new(issued, copiedAssignments, copiedSelections));
            }
            return result;
        }, cancellationToken);
    }

    internal bool IsCreationPrerequisitePreviewCurrent(CharacterCreationPrerequisitePreview preview)
        => _prerequisitePreviews.TryGetValue(preview, out var issued)
           && !issued.ConfirmationStarted && IsCreationPrerequisiteStateCurrent(issued.Load.State);

    internal bool CanDisplayCreationPrerequisitePreview(CharacterCreationPrerequisitePreview preview)
        => _prerequisitePreviews.TryGetValue(preview, out var issued)
           && IsPrerequisiteOriginalOwnerVisible(issued.Load.OriginalDisplay);

    internal Task<CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>>
        RevalidateCreationPrerequisitePreviewAsync(CharacterCreationPrerequisitePreview preview,
            CancellationToken cancellationToken = default, Func<bool>? isCurrentPage = null)
        => _prerequisitePreviews.TryGetValue(preview, out var issued) && !issued.ConfirmationStarted
            ? RevalidateCreationPrerequisiteAsync(issued.Load.State, cancellationToken, isCurrentPage)
            : Task.FromResult(PrerequisiteUnavailable<CharacterCreationPrerequisiteState>());

    private Task<CreationPrerequisitePhoneConfirmResult> ConfirmIssuedCreationPrerequisiteAsync(
        CharacterCreationPrerequisitePreview preview, IReadOnlyDictionary<string, string> assignments,
        CreationPrerequisitePhoneSelections selections, CancellationToken cancellationToken,
        Func<bool>? isCurrentPreview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(selections);
        var copiedAssignments = CopyPrerequisiteAssignments(assignments);
        var copiedSelections = CopyPrerequisiteSelections(selections);
        if (!_prerequisitePreviews.TryGetValue(preview, out var issued))
            return Task.FromResult(Rejected(CharacterCreationPrerequisiteBlockers.WorkspaceUnavailable));
        return WithWorkspaceActivationGateAsync(async () =>
        {
            if (issued.ConfirmationStarted)
                return Rejected(PrerequisiteConfirmationAlreadyAttempted);
            if (isCurrentPreview?.Invoke() == false
                || !IsCreationPrerequisitePreviewCurrent(preview)
                || !CreationPrerequisitePhoneAuthority.IsReady(issued.Load.State, State)
                || _ownerBoundPrerequisiteService is not { } service
                || issued.Load.OriginalDisplay.DisplayOwnerContext is not { IsValid: true } owner
                || !PreviewMatchesSelections(preview, copiedAssignments, copiedSelections, issued.Load.State)
                || !preview.RequiresExplicitConfirmation || !preview.CanConfirm || preview.Blockers.Count != 0
                || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(preview.PreviewDigest)
                || copiedAssignments.Count != issued.Assignments.Count
                || copiedAssignments.Any(pair => !issued.Assignments.TryGetValue(pair.Key, out string? rank) || rank != pair.Value)
                || copiedSelections.HeritageSelectionId != issued.Selections.HeritageSelectionId
                || copiedSelections.TalentSelectionId != issued.Selections.TalentSelectionId
                || !copiedSelections.TalentActiveSkillSelectionIds.SequenceEqual(issued.Selections.TalentActiveSkillSelectionIds)
                || !copiedSelections.TalentSkillGroupSelectionIds.SequenceEqual(issued.Selections.TalentSkillGroupSelectionIds))
                return Rejected(CharacterCreationPrerequisiteBlockers.StaleWorkspaceRevision);
            cancellationToken.ThrowIfCancellationRequested();
            var original = issued.Load.OriginalDisplay;
            var request = new CharacterCreationPrerequisiteConfirmRequest(
                preview.Binding, copiedAssignments, preview.PreviewDigest, ExplicitlyConfirmed: true)
            {
                HeritageSelectionId = copiedSelections.HeritageSelectionId,
                TalentSelectionId = copiedSelections.TalentSelectionId,
                TalentActiveSkillSelectionIds = copiedSelections.TalentActiveSkillSelectionIds,
                TalentSkillGroupSelectionIds = copiedSelections.TalentSkillGroupSelectionIds
            };
            issued.ConfirmationStarted = true;
            int entered = 0;
            CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt> result;
            try
            {
                result = await Task.Run(() =>
                {
                    Interlocked.Exchange(ref entered, 1);
                    return service.Confirm(owner, request);
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (Volatile.Read(ref entered) == 0)
            {
                // Task cancellation proved that this delegate never entered Core.
                // There is no possibly committed operation to classify or recover.
                issued.ConfirmationStarted = false;
                throw;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // No idempotency lookup exists for this domain. A lost return is
                // unknown, never permission to call Confirm again.
                return new("outcome-unknown", null, null, [PrerequisiteOutcomeUnknown]);
            }
            if (result is not { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } receipt })
                return new(result.Outcome, result.Value, null, result.Blockers);
            _prerequisiteReceipts.GetValue(receipt, _ => original);
            try
            {
                if (!IsPrerequisiteOriginalOwnerVisible(original)
                    || _presenter is not IOwnerBoundWorkspaceRefreshPresenter refresh)
                    return CommittedNeedsRefresh();
                await refresh.LoadAsync(owner, receipt.WorkspaceId, cancellationToken);
                if (!CanDisplayCreationPrerequisiteReceipt(receipt)) return CommittedNeedsRefresh();
                await SyncShellAsync(cancellationToken);
                if (!CanDisplayCreationPrerequisiteReceipt(receipt)) return CommittedNeedsRefresh();
                var refreshedDisplay = State;
                if (refreshedDisplay.DisplayOwnerContext != owner
                    || refreshedDisplay.Session.OwnerContext != owner
                    || refreshedDisplay.WorkspaceId != original.WorkspaceId)
                    return CommittedNeedsRefresh();
                var loaded = await Task.Run(() => LoadCreationPrerequisiteInBackground(refreshedDisplay), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!CanDisplayCreationPrerequisiteReceipt(receipt)) return CommittedNeedsRefresh();
                var refreshed = AcceptCreationPrerequisiteLoad(loaded);
                if (refreshed.Value is not { } state
                    || !CreationPrerequisitePhoneAuthority.ReceiptMatches(receipt, state, State))
                    return CommittedNeedsRefresh();
                _notice = "Creation-method draft saved. Core has opened the Attributes prerequisite.";
                NotifyChanged();
                if (!IsCreationPrerequisiteReceiptCurrent(receipt, state)) return CommittedNeedsRefresh();
                return new(result.Outcome, receipt, state, []);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // A known commit remains known when reload, cancellation, an owner
                // transition or an observer fails. Nothing below can replay Confirm.
                return CommittedNeedsRefresh();
            }
            CreationPrerequisitePhoneConfirmResult CommittedNeedsRefresh()
                => new(result.Outcome, receipt, null, [PrerequisitePostCommitRefreshRequired]);
        }, cancellationToken);

        static CreationPrerequisitePhoneConfirmResult Rejected(string blocker)
            => new(CharacterCreationFoundationOutcomes.Conflict, null, null, [blocker]);
    }

    private bool IsPrerequisiteOriginalOwnerVisible(CharacterOverviewState original)
        => !_disposed && State.WorkspaceId == original.WorkspaceId
           && State.DisplayOwnerContext == original.DisplayOwnerContext
           && State.Session.OwnerContext == original.DisplayOwnerContext
           && IsNativePersistenceOwnerCurrent(original.DisplayOwnerContext);

    internal bool CanDisplayCreationPrerequisiteReceipt(CharacterCreationPrerequisiteReceipt receipt)
        => _prerequisiteReceipts.TryGetValue(receipt, out var original)
           && receipt.WorkspaceId == original.WorkspaceId
           && IsPrerequisiteOriginalOwnerVisible(original);

    internal bool IsCreationPrerequisiteReceiptCurrent(CharacterCreationPrerequisiteReceipt receipt,
        CharacterCreationPrerequisiteState state)
        => CanDisplayCreationPrerequisiteReceipt(receipt)
           && IsCreationPrerequisiteStateCurrent(state)
           && CreationPrerequisitePhoneAuthority.ReceiptMatches(receipt, state, State);
}
